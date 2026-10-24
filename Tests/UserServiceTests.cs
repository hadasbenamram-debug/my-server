using BLL.Algorithms;
using BLL.Interfaces;
using BLL.Notifications;
using BLL.Security;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // UserService עם Repositories אמיתיים על מסד בזיכרון: נעילה, טוקנים חד-פעמיים וחותמת אבטחה נשמרים במסד
    public sealed class UserServiceTests : IDisposable
    {
        private readonly EngineHarness _h = new();
        private FoodRescueDbContext Db => _h.Db;

        private UserService Create() => new(
            new UserRepository(Db), new NgoRepository(Db), new SupplierRepository(Db),
            new LoginLockoutService(new LoginAttemptRepository(Db)),
            _h.ActionTokens, _h.NotificationService,
            new TokenService(TestDb.Config()), TestDb.Config(),
            TestDb.Log<UserService>(), TestDb.Mapper, new PasswordHasherWithLegacySupport());

        private async Task<User> AddUserAsync(string email = "a@b.com", string password = "correct-password", UserRole role = UserRole.User)
        {
            var user = EngineHarness.NewUser(email, role);
            user.PasswordHash = new PasswordHasherWithLegacySupport().HashPassword(user, password);
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user;
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public async Task Register_NormalizesEmail_ForcesUserRole_AndDoesNotReturnToken()
        {
            var result = await Create().RegisterAsync(new RegisterDto
            {
                Name = " Dana ", Email = "  Dana@Example.COM ", Phone = "0501234567", Password = "longenough1"
            });

            Assert.True(result.Succeeded);
            Assert.Null(result.Response);   // את הטוקן מקבלים רק בהתחברות
            var saved = await Db.Users.SingleAsync();
            Assert.Equal("dana@example.com", saved.Email);
            Assert.Equal(UserRole.User, saved.Role);
            Assert.NotEqual("longenough1", saved.PasswordHash);
            Assert.False(string.IsNullOrEmpty(saved.SecurityStamp));
        }

        [Fact]
        public async Task Register_ExistingEmail_ReturnsEmailTaken()
        {
            await AddUserAsync("a@b.com");

            var result = await Create().RegisterAsync(new RegisterDto
            {
                Name = "A B", Email = "A@B.com", Phone = "0501234567", Password = "longenough1"
            });

            Assert.Equal(AuthError.EmailTaken, result.Error);
            Assert.Single(await Db.Users.ToListAsync());
        }

        [Fact]
        public async Task Login_WrongPassword_ReturnsUniformInvalidCredentials()
        {
            await AddUserAsync();

            var wrong = await Create().LoginAsync(new LoginDto { Email = "A@B.com", Password = "nope" }, "1.1.1.1");
            var unknown = await Create().LoginAsync(new LoginDto { Email = "ghost@b.com", Password = "nope" }, "1.1.1.1");

            Assert.Equal(AuthError.InvalidCredentials, wrong.Error);
            Assert.Equal(wrong.Message, unknown.Message);
        }

        [Fact]
        public async Task Login_FiveFailures_LocksAccountFromThatIp_EvenForCorrectPassword()
        {
            await AddUserAsync();

            for (int i = 0; i < 5; i++)
                await Create().LoginAsync(new LoginDto { Email = "a@b.com", Password = "bad" }, "1.1.1.1");

            var result = await Create().LoginAsync(new LoginDto { Email = "a@b.com", Password = "correct-password" }, "1.1.1.1");

            Assert.Equal(AuthError.LockedOut, result.Error);
            // הנעילה נשמרת במסד (לא ב-MemoryCache) ולכן שורדת הפעלה מחדש של השרת
            Assert.True(await Db.LoginAttempts.CountAsync() >= 5);
        }

        // תוקן: כל אחד יכול היה לנעול חשבון של משתמש אחר (הנעילה הייתה לפי מייל בלבד)
        [Fact]
        public async Task Login_FailuresFromAnotherIp_DoNotLockTheRealOwner()
        {
            await AddUserAsync();

            for (int i = 0; i < 10; i++)
                await Create().LoginAsync(new LoginDto { Email = "a@b.com", Password = "bad" }, "6.6.6.6");

            var owner = await Create().LoginAsync(new LoginDto { Email = "a@b.com", Password = "correct-password" }, "2.2.2.2");

            Assert.True(owner.Succeeded);
        }

        [Fact]
        public async Task Login_Correct_ReturnsTokenAndRole()
        {
            await AddUserAsync();

            var result = await Create().LoginAsync(new LoginDto { Email = "a@b.com", Password = "correct-password" });

            Assert.True(result.Succeeded);
            Assert.Equal("User", result.Response!.Role);
            Assert.False(result.Response.MustChangePassword);
        }

        [Fact]
        public async Task Login_WithOldFormatHash_Succeeds_AndUpgradesToMicrosoftHasher()
        {
            // hash שנוצר בפורמט הישן של הפרויקט (PBKDF2-SHA256, "iterations.salt.hash") עבור "old-format-pass1"
            var user = EngineHarness.NewUser("old@b.com", UserRole.User);
            user.PasswordHash = "100000.AAECAwQFBgcICQoLDA0ODw==.C7tBXPpxGVlSBJYBoyOCvpS0AfymuEK578lqwCTWJ7c=";
            Db.Users.Add(user);
            await Db.SaveChangesAsync();

            var wrong = await Create().LoginAsync(new LoginDto { Email = "old@b.com", Password = "not-the-password" });
            Assert.False(wrong.Succeeded);

            var result = await Create().LoginAsync(new LoginDto { Email = "old@b.com", Password = "old-format-pass1" });

            Assert.True(result.Succeeded);
            var saved = await Db.Users.AsNoTracking().SingleAsync(u => u.Email == "old@b.com");
            Assert.DoesNotContain(".", saved.PasswordHash);   // נשמר מחדש בפורמט של Microsoft
            Assert.Equal(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success,
                new PasswordHasherWithLegacySupport().VerifyHashedPassword(saved, saved.PasswordHash, "old-format-pass1"));
        }

        [Fact]
        public async Task Login_WithTemporaryPassword_FlagsMustChangePassword()
        {
            var user = await AddUserAsync(role: UserRole.Volunteer);
            user.MustChangePassword = true;
            await Db.SaveChangesAsync();

            var result = await Create().LoginAsync(new LoginDto { Email = "a@b.com", Password = "correct-password" });

            Assert.True(result.Succeeded);
            Assert.True(result.Response!.MustChangePassword);
        }

        [Fact]
        public async Task Login_PendingNgo_IsBlocked()
        {
            var user = await AddUserAsync(role: UserRole.Ngo);
            Db.Regions.Add(new Region { Id = 1, RegionName = "מרכז" });
            Db.Ngos.Add(new Ngo
            {
                UserId = user.Id, NgoName = "N", DailyCapacity = 1, Address = "A", GooglePlaceId = "N/A",
                RegistrationNumber = "1", RegionId = 1, ApprovalStatus = DAL.Classes.ApprovalStatus.Pending
            });
            await Db.SaveChangesAsync();

            var result = await Create().LoginAsync(new LoginDto { Email = "a@b.com", Password = "correct-password" });

            Assert.Equal(AuthError.NgoPendingApproval, result.Error);
        }

        [Fact]
        public async Task ResetPassword_UnknownToken_ReturnsFalse()
        {
            Assert.False(await Create().ResetPasswordAsync(new ResetPasswordDto { Token = "x", NewPassword = "longenough1" }));
        }

        // קישור האיפוס: חד-פעמי, ושינוי הסיסמה מבטל את כל הטוקנים הקודמים (החותמת מתחלפת)
        [Fact]
        public async Task ResetPassword_IsSingleUse_AndRotatesSecurityStamp()
        {
            var user = await AddUserAsync();
            var oldStamp = user.SecurityStamp;

            await Create().ForgotPasswordAsync(new ForgotPasswordDto { Email = "a@b.com" });

            // הקישור נשלח במייל (בתור, מוצפן) - מוציאים ממנו את הטוקן
            _h.Fresh();
            var email = await Db.Notifications.SingleAsync(n => n.UserId == user.Id && n.Channel == NotificationChannel.Email);
            var body = _h.Protector.Unprotect(email.ProtectedPayload!);
            var token = Uri.UnescapeDataString(body.Split("token=")[1].Split('"', '&', '<', '\'', ' ')[0]);

            // בטבלה נשמר רק Hash של הטוקן
            Assert.DoesNotContain(await Db.ActionTokens.ToListAsync(), t => t.TokenHash == token);

            Assert.True(await Create().ResetPasswordAsync(new ResetPasswordDto { Token = token, NewPassword = "brand-new-pass1" }));
            Assert.False(await Create().ResetPasswordAsync(new ResetPasswordDto { Token = token, NewPassword = "another-pass1" }));

            _h.Fresh();
            var updated = await Db.Users.SingleAsync(u => u.Id == user.Id);
            Assert.NotEqual(oldStamp, updated.SecurityStamp);
            Assert.NotEqual(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed,
                new PasswordHasherWithLegacySupport().VerifyHashedPassword(updated, updated.PasswordHash, "brand-new-pass1"));
        }

        [Fact]
        public async Task ChangePassword_RotatesStamp_ClearsTemporaryFlag_AndReturnsNewToken()
        {
            var user = await AddUserAsync();
            user.MustChangePassword = true;
            await Db.SaveChangesAsync();
            var oldStamp = user.SecurityStamp;

            var outcome = await Create().ChangePasswordAsync(user.Id, new ChangePasswordDto
            {
                CurrentPassword = "correct-password", NewPassword = "brand-new-pass1"
            });

            Assert.Equal(ChangePasswordResult.Success, outcome.Result);
            Assert.False(string.IsNullOrEmpty(outcome.NewToken?.Token));

            _h.Fresh();
            var updated = await Db.Users.SingleAsync(u => u.Id == user.Id);
            Assert.NotEqual(oldStamp, updated.SecurityStamp);
            Assert.False(updated.MustChangePassword);
        }

        [Fact]
        public async Task ChangePassword_WrongCurrentPassword_IsRejected()
        {
            var user = await AddUserAsync();

            var outcome = await Create().ChangePasswordAsync(user.Id, new ChangePasswordDto
            {
                CurrentPassword = "wrong", NewPassword = "brand-new-pass1"
            });

            Assert.Equal(ChangePasswordResult.WrongCurrentPassword, outcome.Result);
        }

        // קודי אימות נשמרים מגובבים במסד (לא ב-MemoryCache) ונכונים לפעם אחת
        [Fact]
        public async Task EmailVerificationCode_IsHashed_AndSingleUse()
        {
            var user = await AddUserAsync();
            user.IsEmailVerified = false;
            await Db.SaveChangesAsync();

            Assert.Equal(VerificationResult.Success, await Create().SendEmailVerificationAsync(user.Id));

            _h.Fresh();
            var email = await Db.Notifications.SingleAsync(n => n.UserId == user.Id && n.Channel == NotificationChannel.Email);
            var code = System.Text.RegularExpressions.Regex.Match(_h.Protector.Unprotect(email.ProtectedPayload!), @"\b\d{6}\b").Value;
            Assert.Equal(6, code.Length);
            Assert.DoesNotContain(await Db.ActionTokens.ToListAsync(), t => t.TokenHash.Contains(code));

            Assert.Equal(VerificationResult.InvalidCode, await Create().VerifyEmailAsync(user.Id, "000000" == code ? "111111" : "000000"));
            Assert.Equal(VerificationResult.Success, await Create().VerifyEmailAsync(user.Id, code));

            _h.Fresh();
            Assert.True((await Db.Users.SingleAsync(u => u.Id == user.Id)).IsEmailVerified);
        }

        [Fact]
        public async Task UpdateProfile_ChangingPhone_ResetsPhoneVerification()
        {
            var user = await AddUserAsync();
            user.IsPhoneVerified = true;
            await Db.SaveChangesAsync();

            var profile = await Create().UpdateMyProfileAsync(user.Id, new UpdateMyProfileDto { Name = "חדש", Phone = "0529999999" });

            Assert.NotNull(profile);
            Assert.False(profile!.IsPhoneVerified);
        }

        // תוקן: UserDto.Username היה תמיד null (בישות השדה נקרא Name)
        [Fact]
        public async Task GetById_ReturnsTheName()
        {
            var user = await AddUserAsync();
            var dto = await Create().GetByIdAsync(user.Id);
            Assert.Equal(user.Name, dto!.Name);
            Assert.Equal("User", dto.Role);
        }
    }
}
