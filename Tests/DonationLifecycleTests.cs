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
            _h.ActionTokens, _h.Workflow, _h.UnitOfWork, _h.Policy, TestDb.Mapper);

        private CurrentUserDto SupplierUser => new() { UserId = _h.Supplier.UserId };
        private CurrentUserDto Admin => new() { UserId = _h.AdminUser.Id, IsAdmin = true };

        // שעת איסוף עתידית לפי השעון האמיתי (השירות משווה ל-DateTime.UtcNow). null אם קרוב מדי לחצות.
        private static TimeSpan? PickupLaterToday()
        {
            var local = LocalClock.ToLocal(DateTime.UtcNow);
            if (local.TimeOfDay > new TimeSpan(23, 0, 0)) return null;
            return new TimeSpan(local.Hour + 1 > 23 ? 23 : local.Hour + 1, 30, 0);
        }

        private CreateDonationDto NewDonationDto(TimeSpan pickup) => new()
        {
            RegionId = _h.Region.Id, Latitude = 32.08, Longitude = 34.78, LatestPickupTime = pickup,
            Categories = new List<DonationCategoryLineDto> { new() { CategoryId = _h.Category.Id, Quantity = 3, WeightKg = 4.5 } }
        };

        // תוקן: CreatedAt נשמר כ-UtcNow+3h
        [Fact]
        public async Task Create_StoresCreatedAtInUtc_AndDefaultsExpirationToEndOfTheLocalDay()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();
            if (pickup == null) return;   // אין שעת איסוף אפשרית היום

            var before = DateTime.UtcNow;
            var created = await Donations().CreateAsync(NewDonationDto(pickup.Value), SupplierUser);
            var after = DateTime.UtcNow;

            _h.Fresh();
            var saved = await _h.Db.Donations.SingleAsync();
            Assert.InRange(saved.CreatedAt, before.AddSeconds(-1), after.AddSeconds(1));
            Assert.Equal(DonationStatus.Pending, saved.Status);
            Assert.Equal(LocalClock.ToLocal(saved.CreatedAt).Date, LocalClock.ToLocal(saved.ExpirationTime).Date);

            Assert.Equal("Pending", created.Status);
            Assert.Equal(4.5, created.TotalWeightKg, 3);
            Assert.Equal("מאפים", created.Categories.Single().CategoryName);
            Assert.Equal(_h.Supplier.BusinessName, created.SupplierName);
        }

        [Fact]
        public async Task Create_WithPickupTimeInThePast_IsRejected()
        {
            await _h.SeedBasicsAsync();
            var local = LocalClock.ToLocal(DateTime.UtcNow);
            if (local.TimeOfDay < TimeSpan.FromMinutes(5)) return;

            await Assert.ThrowsAsync<ArgumentException>(() =>
                Donations().CreateAsync(NewDonationDto(TimeSpan.Zero), SupplierUser));
        }

        // חדש: הספק עורך ומבטל תרומות שלו
        [Fact]
        public async Task Supplier_CanEditOwnPendingDonation()
        {
            await _h.SeedBasicsAsync();
            var pickup = PickupLaterToday();
            if (pickup == null) return;
            var created = await Donations().CreateAsync(NewDonationDto(pickup.Value), SupplierUser);
            _h.Fresh();

            var updated = await Donations().UpdateAsync(created.Id, new UpdateDonationDto
            {
                LatestPickupTime = pickup.Value,
                Categories = new List<DonationCategoryLineDto> { new() { CategoryId = _h.Category.Id, Quantity = 1, WeightKg = 2 } }
            }, SupplierUser);

            Assert.Equal(2, updated.TotalWeightKg, 3);
            _h.Fresh();
            Assert.Equal(2, (await _h.Db.DonationFoodCategories.SingleAsync()).WeightKg, 3);
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
            expired.ExpirationTime = DateTime.UtcNow.AddMinutes(-5);
            fresh.ExpirationTime = DateTime.UtcNow.AddHours(5);
            await _h.Db.SaveChangesAsync();
            _h.Fresh();

            var jobs = new MaintenanceJobs(_h.Donations, _h.ActionTokenRepo, new LoginAttemptRepository(_h.Db), _h.Workflow, _h.UnitOfWork, TestDb.Log<MaintenanceJobs>());
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
