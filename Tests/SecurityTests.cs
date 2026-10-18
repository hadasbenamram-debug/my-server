using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Notifications;
using BLL.Security;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Interfaces;
using DAL.Repositories;
using FoodRescue.DTOs;
using FoodRescue.Middlewares;
using FoodRescue.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    public class SecurityTests
    {
        // ---------------- חותמת אבטחה: טוקן ישן אחרי שינוי סיסמה ----------------

        private static async Task<(TokenValidatedContext Context, FoodRescueDbContext Db)> ValidateAsync(
            Action<User> arrangeUser, string tokenStamp, string tokenRole = "User")
        {
            var db = TestDb.New();
            var user = EngineHarness.NewUser("u@test", UserRole.Volunteer);
            user.SecurityStamp = "current-stamp";
            arrangeUser(user);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            // ה-API פונה ל-Service (BLL), וה-Service ל-Repository
            services.AddSingleton<ISessionValidationService>(new SessionValidationService(new UserRepository(db)));
            var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, tokenRole),
                new Claim(TokenService.SecurityStampClaimType, tokenStamp)
            }, "Bearer");

            var context = new TokenValidatedContext(http,
                new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
                new JwtBearerOptions())
            {
                Principal = new ClaimsPrincipal(identity)
            };

            await SecurityStampValidator.ValidateAsync(context);
            return (context, db);
        }

        [Fact]
        public async Task TokenWithCurrentStamp_IsAccepted_AndRoleComesFromTheDatabase()
        {
            var (ctx, db) = await ValidateAsync(_ => { }, "current-stamp", tokenRole: "User");
            using var _ = db;

            Assert.Null(ctx.Result?.Failure);
            Assert.True(ctx.Principal!.IsInRole("Volunteer"));
            Assert.False(ctx.Principal.IsInRole("User"));
        }

        // תוקן: טוקנים ישנים נשארו בתוקף אחרי שינוי סיסמה
        [Fact]
        public async Task TokenWithOldStamp_IsRejected()
        {
            var (ctx, db) = await ValidateAsync(_ => { }, "stamp-before-password-change");
            using var _ = db;

            Assert.NotNull(ctx.Result?.Failure);
        }

        [Fact]
        public async Task TemporaryPassword_AddsThePasswordChangeRequiredClaim()
        {
            var (ctx, db) = await ValidateAsync(u => u.MustChangePassword = true, "current-stamp");
            using var _ = db;

            Assert.True(ctx.Principal!.HasClaim(SecurityStampValidator.PasswordChangeRequiredClaim, "true"));
        }

        [Theory]
        [InlineData("/api/auth/change-password", true)]
        [InlineData("/api/users/me", true)]
        [InlineData("/notificationHub", true)]
        [InlineData("/api/donations", false)]
        [InlineData("/api/matches/active", false)]
        public void PasswordChangeRequired_AllowsOnlyTheChangePasswordFlow(string path, bool allowed)
        {
            Assert.Equal(allowed, PasswordChangeRequiredMiddleware.IsAllowed(new PathString(path)));
        }

        // ---------------- מבנה שגיאה אחיד ----------------

        [Fact]
        public void ExceptionMapping_UsesOneShape_WithTheRightStatusCodes()
        {
            Assert.Equal(404, ExceptionHandlingMiddleware.Map(new NotFoundException("x"), null).Status);
            Assert.Equal(401, ExceptionHandlingMiddleware.Map(new UnauthorizedException("x"), null).Status);
            Assert.Equal(403, ExceptionHandlingMiddleware.Map(new ForbiddenException("x"), null).Status);
            Assert.Equal(409, ExceptionHandlingMiddleware.Map(new ConflictException("x"), null).Status);
            Assert.Equal(409, ExceptionHandlingMiddleware.Map(new DbUpdateConcurrencyException("x"), null).Status);

            var bad = ExceptionHandlingMiddleware.Map(new ArgumentException("שגיאה", "field"), "trace");
            Assert.Equal(400, bad.Status);
            Assert.Equal("שגיאה", bad.Message);
            Assert.Equal("trace", bad.TraceId);

            var server = ExceptionHandlingMiddleware.Map(new InvalidOperationException("secret details"), null, includeDetails: false);
            Assert.Equal(500, server.Status);
            Assert.DoesNotContain("secret details", server.Message);
        }

        // ---------------- הצפנת גוף ההודעות בטבלת ההתראות ----------------

        [Fact]
        public void PayloadProtector_RoundTrips_AndDetectsTampering()
        {
            var protector = new AesGcmPayloadProtector(RandomNumberGenerator.GetBytes(32));

            var a = protector.Protect("סיסמה זמנית: Abc123!");
            var b = protector.Protect("סיסמה זמנית: Abc123!");

            Assert.StartsWith("v1:", a);
            Assert.NotEqual(a, b);   // nonce אקראי
            Assert.DoesNotContain("Abc123", a);
            Assert.Equal("סיסמה זמנית: Abc123!", protector.Unprotect(a));

            var tampered = a.Substring(0, a.Length - 4) + (a.EndsWith("AAAA") ? "BBBB" : "AAAA");
            Assert.ThrowsAny<Exception>(() => protector.Unprotect(tampered));
        }

        // תוקן: טבלת Notifications שמרה את גוף המייל (סיסמאות זמניות, קודים) כטקסט גלוי
        [Fact]
        public async Task QueuedEmail_StoresOnlyASafeSummary_AndAnEncryptedBody()
        {
            using var h = new EngineHarness();
            await h.NotificationService.QueueEmailAsync(null, "guest@test", "נושא", "הסיסמה הזמנית שלך: Secret-987", summary: "נשלחו פרטי התחברות");

            var n = await h.Db.Notifications.SingleAsync();
            Assert.Equal("נשלחו פרטי התחברות", n.Content);
            Assert.DoesNotContain("Secret-987", n.ProtectedPayload!);
            Assert.Equal(NotificationStatus.Pending, n.Status);
            Assert.True(h.Jobs.Has<NotificationDispatcher>(nameof(NotificationDispatcher.DispatchAsync)));
        }

        // תוקן: פנייה מאורח נרשמה על משתמש 1
        [Fact]
        public async Task GuestContactMessage_IsNotAttributedToUserOne()
        {
            using var h = new EngineHarness();
            h.Db.Users.Add(EngineHarness.NewUser("first-user@test", UserRole.Admin));
            await h.Db.SaveChangesAsync();

            var support = new SupportService(h.NotificationService,
                TestDb.Config(new() { ["SupportSettings:InboxEmail"] = "support@test" }), TestDb.Log<SupportService>());

            await support.SendContactMessageAsync(new ContactMessageDto
            {
                Name = "אורח", Email = "guest@test", Subject = "שאלה", Message = "שלום"
            }, userId: null);

            var rows = await h.Db.Notifications.ToListAsync();
            Assert.Equal(2, rows.Count);   // לצוות + אישור קבלה לפונה
            Assert.All(rows, r => Assert.Null(r.UserId));
        }

        // שליחה ברקע עם ניסיונות חוזרים, וגוף ההודעה נמחק אחרי השליחה
        [Fact]
        public async Task Dispatcher_RetriesWithBackoff_ThenWipesTheBodyAfterSending()
        {
            using var h = new EngineHarness();
            await h.NotificationService.QueueEmailAsync(null, "x@test", "נושא", "גוף", summary: "תקציר");
            var id = (await h.Db.Notifications.SingleAsync()).Id;

            var sender = new FlakyEmailSender(failures: 1);
            var dispatcher = new NotificationDispatcher(h.Notifications, h.Protector, sender, new NoSms(), TestDb.Log<NotificationDispatcher>());

            await dispatcher.DispatchAsync(id);
            h.Fresh();
            var afterFailure = await h.Db.Notifications.SingleAsync();
            Assert.Equal(NotificationStatus.Pending, afterFailure.Status);
            Assert.Equal(1, afterFailure.RetryCount);
            Assert.True(afterFailure.NextAttemptAt > DateTime.UtcNow);

            // לפני זמן הניסיון הבא - לא נשלח שוב
            await dispatcher.DispatchAsync(id);
            Assert.Equal(1, sender.Calls);

            // הזמן עבר
            afterFailure.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await h.Db.SaveChangesAsync();
            h.Fresh();

            await dispatcher.DispatchAsync(id);
            h.Fresh();
            var sent = await h.Db.Notifications.SingleAsync();
            Assert.Equal(NotificationStatus.Sent, sent.Status);
            Assert.Null(sent.ProtectedPayload);
            Assert.Equal("msg-1", sent.ExternalMessageId);
        }

        private sealed class FlakyEmailSender : IEmailSender
        {
            private int _failures;
            public int Calls { get; private set; }
            public FlakyEmailSender(int failures) => _failures = failures;

            public Task<string?> SendAsync(string to, string subject, string htmlBody)
            {
                Calls++;
                if (_failures-- > 0) throw new InvalidOperationException("SMTP down");
                return Task.FromResult<string?>("msg-1");
            }
        }

        private sealed class NoSms : ISmsSender
        {
            public Task<string?> SendAsync(string to, string text) => Task.FromResult<string?>(null);
        }

        // ---------------- טוקנים חד-פעמיים ----------------

        [Fact]
        public async Task LinkToken_IsStoredHashed_ScopedToItsTarget_AndSingleUse()
        {
            using var db = TestDb.New();
            var service = new ActionTokenService(new ActionTokenRepository(db));

            var raw = await service.CreateLinkTokenAsync(5, ActionTokenPurpose.MatchResponse, TimeSpan.FromMinutes(5), targetId: 77);

            var stored = await db.ActionTokens.SingleAsync();
            Assert.NotEqual(raw, stored.TokenHash);
            Assert.Equal(77, stored.TargetId);

            var found = await service.FindValidLinkTokenAsync(ActionTokenPurpose.MatchResponse, raw);
            Assert.NotNull(found);
            Assert.Null(await service.FindValidLinkTokenAsync(ActionTokenPurpose.PasswordReset, raw));   // מטרה אחרת

            await service.ConsumeAsync(found!);
            Assert.Null(await service.FindValidLinkTokenAsync(ActionTokenPurpose.MatchResponse, raw));
        }

        [Fact]
        public async Task LinkToken_Expires()
        {
            using var db = TestDb.New();
            var clock = new TestClock(new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc));
            var service = new ActionTokenService(new ActionTokenRepository(db), clock);

            var raw = await service.CreateLinkTokenAsync(5, ActionTokenPurpose.PasswordReset, TimeSpan.FromMinutes(30));
            clock.Advance(TimeSpan.FromMinutes(31));

            Assert.Null(await service.FindValidLinkTokenAsync(ActionTokenPurpose.PasswordReset, raw));
        }

        [Fact]
        public async Task VerificationCode_LocksAfterTooManyWrongAttempts()
        {
            using var db = TestDb.New();
            var service = new ActionTokenService(new ActionTokenRepository(db));
            var code = await service.CreateCodeAsync(3, ActionTokenPurpose.PhoneVerification, TimeSpan.FromMinutes(10));
            var wrong = code == "123456" ? "654321" : "123456";

            for (int i = 0; i < 5; i++)
                await service.VerifyCodeAsync(3, ActionTokenPurpose.PhoneVerification, wrong, maxAttempts: 5);

            Assert.NotEqual(CodeCheckResult.Valid, await service.VerifyCodeAsync(3, ActionTokenPurpose.PhoneVerification, code, maxAttempts: 5));
        }

        // ---------------- נעילת התחברות ----------------

        [Fact]
        public async Task Lockout_IsPerAccountAndIp_AndPerIpForSpraying()
        {
            using var db = TestDb.New();
            var clock = new TestClock(new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc));
            var lockout = new LoginLockoutService(new LoginAttemptRepository(db), clock);

            for (int i = 0; i < LoginLockoutService.MaxFailuresPerAccountAndIp; i++)
                await lockout.RecordAsync("victim@test", "6.6.6.6", null, success: false);

            Assert.NotNull(await lockout.GetLockRemainingAsync("victim@test", "6.6.6.6"));
            Assert.Null(await lockout.GetLockRemainingAsync("victim@test", "1.2.3.4"));

            // הרבה חשבונות מאותה כתובת - הכתובת נחסמת
            for (int i = 0; i < LoginLockoutService.MaxFailuresPerIp; i++)
                await lockout.RecordAsync($"user{i}@test", "9.9.9.9", null, success: false);
            Assert.NotNull(await lockout.GetLockRemainingAsync("someone-new@test", "9.9.9.9"));

            // אחרי חלון הזמן הנעילה משתחררת
            clock.Advance(LoginLockoutService.Window + TimeSpan.FromMinutes(1));
            Assert.Null(await lockout.GetLockRemainingAsync("victim@test", "6.6.6.6"));
        }
    }
}
