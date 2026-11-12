using BLL.Algorithms;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Services;
using DAL;
using DAL.Classes;
using DAL.Repositories;
using FoodRescue.DTOs;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using MatchStatus = DAL.Classes.MatchStatus;
// גם ב-Moq יש מחלקה בשם Match - כאן Match תמיד השידוך מה-DAL
using Match = DAL.Classes.Match;

namespace FoodRescue.Tests
{
    // יצירה / עריכה / ביטול / תפוגה של תרומות, ותגובות - שכבות אמיתיות על מסד בזיכרון
    public sealed class DonationLifecycleTests : IDisposable
    {
        private readonly EngineHarness _h = new();

        public void Dispose() => _h.Dispose();

        private DonationService Donations() => new(
            _h.Donations, _h.Suppliers, _h.Ngos, _h.Matches, _h.Caller,
            new Mock<IImageService>().Object, new Mock<IImageAnalysisService>().Object,
            new FoodCategoryRepository(_h.Db), new RegionRepository(_h.Db),
            _h.ActionTokens, _h.Workflow, _h.UnitOfWork, _h.Policy, TestDb.Mapper, _h.Clock);

        private CurrentUserDto SupplierUser => new() { UserId = _h.Supplier.UserId };
        private CurrentUserDto Admin => new() { UserId = _h.AdminUser.Id, IsAdmin = true };

        // תיקון 42: שעת איסוף עתידית לפי השעון של המערכת - השעון הקבוע של הבדיקה (יום שני 10:00).
        // בעבר לפי השעון האמיתי, ואחרי 23:00 הבדיקות "עברו" בלי לבדוק כלום.
        private TimeSpan PickupLaterToday() => _h.LocalNow.TimeOfDay + new TimeSpan(3, 30, 0);

        private CreateDonationDto NewDonationDto(TimeSpan pickup) => new()
        {
            RegionId = _h.Region.Id, LatestPickupTime = pickup,
            Categories = new List<DonationCategoryLineDto> { new() { CategoryId = _h.Category.Id, Quantity = 3, WeightKg = 4.5 } }
        };

        // תוקן: CreatedAt נשמר כ-UtcNow+3h
        [Fact]
        public async Task Create_StoresCreatedAtInUtc_AndDefaultsExpirationToEndOfTheLocalDay()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            var created = await Donations().CreateAsync(NewDonationDto(pickup), _h.Supplier.UserId);

            _h.Fresh();
            var saved = await _h.Db.Donations.SingleAsync();
            Assert.Equal(_h.NowUtc, saved.CreatedAt);   // תיקון 42: אותו שעון של כל המערכת
            Assert.Equal(DonationStatus.Pending, saved.Status);
            Assert.Equal(LocalClock.ToLocal(saved.CreatedAt).Date, LocalClock.ToLocal(saved.ExpirationTime).Date);

            Assert.Equal("Pending", created.Status);
            Assert.Equal(4.5, created.TotalWeightKg, 3);
            Assert.Equal("מאפים", created.Categories.Single().CategoryName);
            Assert.Equal(_h.Supplier.BusinessName, created.SupplierName);
        }

        // המיקום של התרומה הוא המיקום המדויק של העסק (שנקבע בשרת לפי הכתובת)
        [Fact]
        public async Task Create_UsesTheSuppliersExactLocation()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            var supplier = await _h.Db.Suppliers.SingleAsync(s => s.Id == _h.Supplier.Id);
            supplier.Latitude = 32.0615;
            supplier.Longitude = 34.7738;
            supplier.GooglePlaceId = "place-of-the-supplier";
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            await Donations().CreateAsync(NewDonationDto(pickup), _h.Supplier.UserId);

            _h.Fresh();
            var saved = await _h.Db.Donations.SingleAsync();
            Assert.Equal(32.0615, saved.Latitude, 6);
            Assert.Equal(34.7738, saved.Longitude, 6);
            Assert.Equal("place-of-the-supplier", saved.GooglePlaceId);
        }

        [Fact]
        public async Task Create_WhenTheSupplierHasNoLocation_IsRejected()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            var supplier = await _h.Db.Suppliers.SingleAsync(s => s.Id == _h.Supplier.Id);
            supplier.Latitude = 0;
            supplier.Longitude = 0;
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            await Assert.ThrowsAsync<ArgumentException>(() => Donations().CreateAsync(NewDonationDto(pickup), _h.Supplier.UserId));
            Assert.False(await _h.Db.Donations.AnyAsync());
        }

        [Fact]
        public async Task Create_WithPickupTimeInThePast_IsRejected()
        {
            await _h.SeedBasicsAsync();
            // השעון הקבוע: 10:00 - חצות כבר עברה
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Donations().CreateAsync(NewDonationDto(TimeSpan.Zero), _h.Supplier.UserId));
        }

        // תיקון 43: "זמינה לאיסוף החל מ-" נשמרת ונבדקת מול שעת האיסוף האחרונה; שעה שכבר עברה לא מגבילה כלום
        [Fact]
        public async Task Create_WithEarliestPickupTime_SavesIt_AndValidatesTheWindow()
        {
            await _h.SeedBasicsAsync();
            var latest = _h.LocalNow.TimeOfDay + TimeSpan.FromHours(6);   // 16:00 (השעון הקבוע: 10:00)
            var from = _h.LocalNow.TimeOfDay + TimeSpan.FromHours(4);     // 14:00

            var dto = NewDonationDto(latest);
            dto.EarliestPickupTime = from;
            var created = await Donations().CreateAsync(dto, _h.Supplier.UserId);

            Assert.Equal<TimeSpan?>(from, created.EarliestPickupTime);
            _h.Fresh();
            Assert.Equal<TimeSpan?>(from, (await _h.Db.Donations.SingleAsync()).EarliestPickupTime);

            // פחות מ-30 דקות לפני שעת האיסוף האחרונה - אין למתנדב חלון אמיתי
            var tooShort = NewDonationDto(latest);
            tooShort.EarliestPickupTime = latest - TimeSpan.FromMinutes(20);
            await Assert.ThrowsAsync<ArgumentException>(() => Donations().CreateAsync(tooShort, _h.Supplier.UserId));

            // שעה שכבר עברה = אפשר לאסוף מיד, ולכן לא נשמרת
            var past = NewDonationDto(latest);
            past.EarliestPickupTime = _h.LocalNow.TimeOfDay - TimeSpan.FromHours(1);
            var immediate = await Donations().CreateAsync(past, _h.Supplier.UserId);
            Assert.Null(immediate.EarliestPickupTime);
        }

        // תיקון 44: מתנדב פותח תרומה לפי המספר שלה - פרטי השידוך (מי המתנדב, איזו עמותה) רק של שידוך שלו
        [Fact]
        public async Task Volunteer_SeesWhoHandlesADonation_OnlyWhenItIsTheirOwnMatch()
        {
            await _h.SeedBasicsAsync();
            var assigned = await _h.AddVolunteerAsync("assigned@test");
            var other = await _h.AddVolunteerAsync("other@test");
            var matched = await _h.AddDonationAsync();
            var open = await _h.AddDonationAsync();

            matched.Status = DonationStatus.Matched;
            _h.Db.Matches.Add(new Match
            {
                DonationId = matched.Id, NgoId = _h.Ngo.Id, VolunteerId = assigned.Id, Status = MatchStatus.Confirmed,
                IsActive = true, AssignedAt = _h.NowUtc.AddMinutes(-5)
            });
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var assignedUser = new CurrentUserDto { UserId = assigned.UserId };
            var otherUser = new CurrentUserDto { UserId = other.UserId };

            // המתנדב של השידוך - רואה את הכול
            var own = await Donations().GetByIdAsync(matched.Id, assignedUser);
            Assert.NotNull(own);
            Assert.Equal(assigned.Id, own!.VolunteerId);
            Assert.Equal(_h.Ngo.Id, own.NgoId);

            // מתנדב אחר - תרומה ששובצה למישהו אחר "לא קיימת" בשבילו
            Assert.Null(await Donations().GetByIdAsync(matched.Id, otherUser));

            // תרומה שעדיין ממתינה לשיבוץ - גלויה (כמו ברשימת התרומות הזמינות), בלי פרטי שידוך
            var available = await Donations().GetByIdAsync(open.Id, otherUser);
            Assert.NotNull(available);
            Assert.Null(available!.VolunteerId);
            Assert.Null(available.VolunteerName);
            Assert.Null(available.MatchId);

            // הספק והמנהל - כמו קודם
            Assert.Equal(assigned.Id, (await Donations().GetByIdAsync(matched.Id, SupplierUser))!.VolunteerId);
            Assert.Equal(assigned.Id, (await Donations().GetByIdAsync(matched.Id, Admin))!.VolunteerId);
        }

        // תיקון 60: מסך התרומה של העמותה / המתנדב - מועדי השלבים לציר "מעקב תרומה"
        [Fact]
        public async Task DonationScreen_GetsTheStageTimes_ForTheTrackingTimeline()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("timeline@test");
            var donation = await _h.AddDonationAsync();
            donation.Status = DonationStatus.Matched;
            var assignedAt = _h.NowUtc.AddMinutes(-50);
            var confirmedAt = _h.NowUtc.AddMinutes(-45);
            var pickedUpAt = _h.NowUtc.AddMinutes(-10);
            _h.Db.Matches.Add(new Match
            {
                DonationId = donation.Id, NgoId = _h.Ngo.Id, VolunteerId = volunteer.Id, Status = MatchStatus.InTransit,
                IsActive = true, AssignedAt = assignedAt, RespondedAt = confirmedAt, PickedUpAt = pickedUpAt
            });
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var dto = await Donations().GetByIdAsync(donation.Id, new CurrentUserDto { UserId = volunteer.UserId });

            Assert.Equal(assignedAt, dto!.MatchAssignedAt);
            Assert.Equal(confirmedAt, dto.VolunteerConfirmedAt);
            Assert.Equal(pickedUpAt, dto.PickedUpAt);
            Assert.Null(dto.DeliveredAt);   // עוד לא נמסר
        }

        // מתנדב שדחה - "ענה" אבל לא אישר: אין "המתנדב אישר" בציר
        [Fact]
        public async Task DonationScreen_DeclinedMatch_HasNoConfirmationTime()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("declined@test");
            var donation = await _h.AddDonationAsync();
            _h.Db.Matches.Add(new Match
            {
                DonationId = donation.Id, NgoId = _h.Ngo.Id, VolunteerId = volunteer.Id, Status = MatchStatus.Declined,
                IsActive = false, AssignedAt = _h.NowUtc.AddMinutes(-20), RespondedAt = _h.NowUtc.AddMinutes(-15)
            });
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var dto = await Donations().GetByIdAsync(donation.Id, Admin);

            Assert.NotNull(dto!.MatchAssignedAt);
            Assert.Null(dto.VolunteerConfirmedAt);
        }

        // תיקון 48: ברשימה של הספק (הדשבורד) - משקל, תכולה, ומי מטפל בכל תרומה (השידוך האחרון שלה)
        [Fact]
        public async Task SupplierList_ShowsWeight_AndWhoHandlesEachDonation()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");
            var matched = await _h.AddDonationAsync(weightKg: 7.5);
            var waiting = await _h.AddDonationAsync(weightKg: 2);

            matched.Status = DonationStatus.Matched;
            _h.Db.Matches.Add(new Match
            {
                DonationId = matched.Id, NgoId = _h.Ngo.Id, VolunteerId = volunteer.Id, Status = MatchStatus.Confirmed,
                IsActive = true, AssignedAt = _h.NowUtc.AddMinutes(-5), EstimatedArrival = _h.NowUtc.AddMinutes(20)
            });
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var list = await Donations().GetMySupplierDonationsAsync(_h.Supplier.UserId);

            var handled = list.Single(d => d.Id == matched.Id);
            Assert.Equal(7.5, handled.TotalWeightKg, 3);
            Assert.Equal("מאפים", handled.Categories.Single().CategoryName);
            Assert.Equal("Confirmed", handled.MatchStatus);
            Assert.Equal(_h.Ngo.NgoName, handled.NgoName);
            Assert.Equal("vol", handled.VolunteerName);   // NewUser: השם הוא החלק שלפני ה-@
            Assert.Equal<DateTime?>(_h.NowUtc.AddMinutes(20), handled.EstimatedArrival);

            var open = list.Single(d => d.Id == waiting.Id);
            Assert.Equal(2, open.TotalWeightKg, 3);
            Assert.Null(open.MatchStatus);
            Assert.Null(open.VolunteerName);
        }

        // חדש: הספק עורך ומבטל תרומות שלו
        [Fact]
        public async Task Supplier_CanEditOwnPendingDonation()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();
            var created = await Donations().CreateAsync(NewDonationDto(pickup), _h.Supplier.UserId);
            _h.Fresh();

            var updated = await Donations().UpdateAsync(created.Id, new UpdateDonationDto
            {
                LatestPickupTime = pickup,
                Categories = new List<DonationCategoryLineDto> { new() { CategoryId = _h.Category.Id, Quantity = 1, WeightKg = 2 } }
            }, SupplierUser);

            Assert.Equal(2, updated.TotalWeightKg, 3);
            _h.Fresh();
            Assert.Equal(2, (await _h.Db.DonationFoodCategories.SingleAsync()).WeightKg, 3);
        }

        // מדריך התמונות: במסד נשמר רק שם הקובץ, בלי נתיב ובלי כתובת
        [Fact]
        public async Task Create_WithUploadedImage_StoresOnlyTheFileName()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            var fileName = Guid.NewGuid().ToString("N") + ".jpg";
            await _h.ActionTokens.RegisterOwnedValueAsync(_h.Supplier.UserId, ActionTokenPurpose.ImageUpload, fileName, TimeSpan.FromMinutes(30));

            var dto = NewDonationDto(pickup);
            dto.Image = fileName;
            var created = await Donations().CreateAsync(dto, _h.Supplier.UserId);

            Assert.Equal(fileName, created.Image);
            _h.Fresh();
            Assert.Equal(fileName, (await _h.Db.Donations.SingleAsync(d => d.Id == created.Id)).Image);
        }

        // תוקן: לחיצה נוספת על "פרסם" (אותה תרומה, אותה תמונה שכבר נוצלה) נכשלה בחריגה.
        // עכשיו מוחזרת התרומה שכבר נוצרה - בלי שגיאה ובלי כפילות.
        [Fact]
        public async Task Create_PublishedTwiceWithTheSameImage_ReturnsTheSameDonation()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            var fileName = Guid.NewGuid().ToString("N") + ".jpg";
            await _h.ActionTokens.RegisterOwnedValueAsync(_h.Supplier.UserId, ActionTokenPurpose.ImageUpload, fileName, TimeSpan.FromMinutes(30));
            var dto = NewDonationDto(pickup);
            dto.Image = fileName;

            var first = await Donations().CreateAsync(dto, _h.Supplier.UserId);
            var second = await Donations().CreateAsync(dto, _h.Supplier.UserId);

            Assert.Equal(first.Id, second.Id);
            _h.Fresh();
            Assert.Single(await _h.Db.Donations.ToListAsync());
        }

        [Fact]
        public async Task Create_WithImagePathInsteadOfFileName_IsRejected()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            var fileName = Guid.NewGuid().ToString("N") + ".jpg";
            await _h.ActionTokens.RegisterOwnedValueAsync(_h.Supplier.UserId, ActionTokenPurpose.ImageUpload, fileName, TimeSpan.FromMinutes(30));

            var dto = NewDonationDto(pickup);
            dto.Image = "/images/" + fileName;
            await Assert.ThrowsAsync<ArgumentException>(() => Donations().CreateAsync(dto, _h.Supplier.UserId));
        }

        [Fact]
        public async Task Create_ImageUploadedByAnotherUser_IsRejected()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            var fileName = Guid.NewGuid().ToString("N") + ".jpg";
            await _h.ActionTokens.RegisterOwnedValueAsync(_h.AdminUser.Id, ActionTokenPurpose.ImageUpload, fileName, TimeSpan.FromMinutes(30));

            var dto = NewDonationDto(pickup);
            dto.Image = fileName;
            await Assert.ThrowsAsync<ArgumentException>(() => Donations().CreateAsync(dto, _h.Supplier.UserId));
        }

        // מדריך JWT (IDOR): הספק נקבע לפי הטוקן. מנהל יוצר בשם ספק רק דרך נקודת הקצה של המנהלים.
        [Fact]
        public async Task CreateForSupplier_ByAdmin_AssignsTheChosenSupplier()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            var created = await Donations().CreateForSupplierAsync(_h.Supplier.Id, NewDonationDto(pickup), _h.AdminUser.Id);

            Assert.Equal(_h.Supplier.Id, created.SupplierId);
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Donations().CreateForSupplierAsync(999999, NewDonationDto(pickup), _h.AdminUser.Id));
        }

        [Fact]
        public async Task Create_ByUserWithoutSupplierProfile_IsForbidden()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                Donations().CreateAsync(NewDonationDto(pickup), _h.AdminUser.Id));
        }

        [Fact]
        public async Task OtherSupplier_CannotEditOrCancel_TheDonation()
        {
            await _h.SeedBasicsAsync();
            var donation = await _h.AddDonationAsync();
            var otherUser = EngineHarness.NewUser("other-supplier@test", UserRole.Supplier);
            _h.Db.Users.Add(otherUser);
            await _h.Db.SaveChangesAsync();
            _h.Db.Suppliers.Add(new Supplier
            {
                UserId = otherUser.Id, BusinessName = "אחר", Address = "ב", TaxId = "987654321", GooglePlaceId = "N/A",
                BusinessLicenseFilePath = "y.pdf", ApprovalStatus = DAL.Classes.ApprovalStatus.Approved
            });
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var other = new CurrentUserDto { UserId = otherUser.Id };
            await Assert.ThrowsAsync<NotFoundException>(() => Donations().CancelAsync(donation.Id, other, null));
            await Assert.ThrowsAsync<NotFoundException>(() => Donations().UpdateAsync(donation.Id, new UpdateDonationDto
            {
                LatestPickupTime = new TimeSpan(20, 0, 0),
                Categories = new List<DonationCategoryLineDto> { new() { CategoryId = _h.Category.Id, Quantity = 1, WeightKg = 1 } }
            }, other));
        }

        [Fact]
        public async Task Supplier_CancelsMatchedDonation_ActiveMatchIsCancelled_AndVolunteerNotified()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");
            var donation = await _h.AddDonationAsync();
            await _h.Engine.RunBatchAsync("Manual");
            _h.Fresh();

            await Donations().CancelAsync(donation.Id, SupplierUser, "הספק סגר מוקדם");

            _h.Fresh();
            Assert.Equal(DonationStatus.Cancelled, (await _h.Db.Donations.SingleAsync()).Status);
            var match = await _h.Db.Matches.SingleAsync();
            Assert.Equal(MatchStatus.Cancelled, match.Status);
            Assert.False(match.IsActive);
            Assert.Contains(await _h.Db.Notifications.ToListAsync(),
                n => n.UserId == volunteer.UserId && n.Channel == NotificationChannel.InApp && n.Content == "הספק סגר מוקדם");
        }

        [Fact]
        public async Task Cancel_AfterPickup_IsAConflict()
        {
            await _h.SeedBasicsAsync();
            var donation = await _h.AddDonationAsync();
            donation.Status = DonationStatus.PickedUp;
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            await Assert.ThrowsAsync<ConflictException>(() => Donations().CancelAsync(donation.Id, Admin, null));
        }

        // תוקן: תרומות שפג תוקפן נשארו Pending
        [Fact]
        public async Task ExpireJob_MarksPastExpirationDonationsAsExpired()
        {
            await _h.SeedBasicsAsync();
            var expired = await _h.AddDonationAsync();
            var fresh = await _h.AddDonationAsync();
            expired.ExpirationTime = _h.NowUtc.AddMinutes(-5);
            fresh.ExpirationTime = _h.NowUtc.AddHours(5);
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var jobs = new MaintenanceJobs(_h.Donations, _h.ActionTokenRepo, new LoginAttemptRepository(_h.Db), _h.Workflow, _h.UnitOfWork,
                TestDb.Log<MaintenanceJobs>(), _h.DeliveryLogs, _h.Clock);
            Assert.Equal(1, await jobs.ExpireDonationsAsync());

            _h.Fresh();
            Assert.Equal(DonationStatus.Expired, (await _h.Db.Donations.SingleAsync(d => d.Id == expired.Id)).Status);
            Assert.Equal(DonationStatus.Pending, (await _h.Db.Donations.SingleAsync(d => d.Id == fresh.Id)).Status);
        }

        // ---------------- תגובות (עץ רקורסיבי) ----------------

        private CommentService Comments() => new(new CommentRepository(_h.Db), _h.Matches, _h.Donations, _h.Users, new CommentTreeService(), _h.Caller);

        [Fact]
        public async Task Comment_WithoutTarget_IsRejected()
        {
            await _h.SeedBasicsAsync();
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Comments().AddAsync(new CreateCommentDto { Content = "שלום" }, SupplierUser));
        }

        [Fact]
        public async Task Comments_FormATree_RepliesInheritTheTarget_AndDeleteRemovesTheSubtree()
        {
            await _h.SeedBasicsAsync();
            var donation = await _h.AddDonationAsync();
            var service = Comments();

            var root = await service.AddAsync(new CreateCommentDto { Content = "האם יש גם לחם?", DonationId = donation.Id }, SupplierUser);
            var reply = await service.AddAsync(new CreateCommentDto { Content = "כן", ParentCommentId = root.Id }, SupplierUser);
            await service.AddAsync(new CreateCommentDto { Content = "תודה", ParentCommentId = reply.Id }, SupplierUser);

            Assert.Equal(donation.Id, reply.DonationId);   // התשובה שייכת לאותה תרומה

            var tree = await service.GetTreeAsync(SupplierUser);
            var top = Assert.Single(tree);
            Assert.Equal("כן", Assert.Single(top.Replies).Text);
            Assert.Equal("תודה", Assert.Single(top.Replies[0].Replies).Text);

            Assert.Equal(3, await service.DeleteAsync(root.Id));
            Assert.Empty(await _h.Db.Comments.ToListAsync());
        }

        [Fact]
        public async Task Comment_OnSomeoneElsesDonation_LooksLikeNotFound()
        {
            await _h.SeedBasicsAsync();
            var donation = await _h.AddDonationAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test", withSlot: false);
            var stranger = new CurrentUserDto { UserId = volunteer.UserId };

            await Assert.ThrowsAsync<NotFoundException>(() =>
                Comments().AddAsync(new CreateCommentDto { Content = "x", DonationId = donation.Id }, stranger));
        }
    }
}
