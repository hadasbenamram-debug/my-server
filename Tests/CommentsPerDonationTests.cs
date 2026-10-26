using BLL.Algorithms;
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
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using MatchStatus = DAL.Classes.MatchStatus;
using RequestedRole = FoodRescue.DTOs.RequestedRole;

namespace FoodRescue.Tests
{
    // תיקון 18: כל תגובה היא על תרומה מסוימת, וברשומה במסד נשמרת התרומה (DonationId חובה)
    public class CommentsPerDonationTests
    {
        private static CommentService Comments(EngineHarness h) =>
            new(new CommentRepository(h.Db), h.Matches, h.Donations, h.Users, new CommentTreeService(), h.Caller);

        private static CurrentUserDto As(int userId) => new() { UserId = userId };

        private static async Task AddMatchAsync(EngineHarness h, Volunteer volunteer, Donation donation, MatchStatus status)
        {
            h.Db.Matches.Add(new Match
            {
                DonationId = donation.Id, NgoId = h.Ngo.Id, VolunteerId = volunteer.Id, Status = status,
                IsActive = status is MatchStatus.Confirmed or MatchStatus.InPickup or MatchStatus.InTransit,
                AssignedAt = h.NowUtc.AddMinutes(-30)
            });
            await h.Db.SaveChangesAsync();
        }

        [Fact]
        public async Task AVolunteersComment_IsSavedWithTheDonation_AndTheSupplierAndNgoSeeItOnThatDonation()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var volunteer = await h.AddVolunteerAsync("vol@test");
            var donation = await h.AddDonationAsync();
            await h.Engine.RunBatchAsync("Manual");
            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            await h.MatchService.AcceptAsync(match.Id, As(volunteer.UserId));
            h.Fresh();

            // בעבר מתנדב הגיב על השיבוץ (MatchId), וברשומה במסד לא הייתה התרומה
            var added = await Comments(h).AddAsync(
                new CreateCommentDto { Content = "אגיע בעוד רבע שעה", DonationId = donation.Id }, As(volunteer.UserId));
            h.Fresh();

            var row = await h.Db.Comments.SingleAsync();
            Assert.Equal(donation.Id, row.DonationId);
            Assert.Equal(donation.Id, added.DonationId);

            foreach (var userId in new[] { h.Supplier.UserId, h.Ngo.UserId, volunteer.UserId })
            {
                var seen = Assert.Single(await Comments(h).GetTreeAsync(As(userId)));
                Assert.Equal(donation.Id, seen.DonationId);
                Assert.Equal("מאפיית בדיקה", seen.DonationSupplierName);
            }
        }

        [Fact]
        public async Task ACommentMustNameADonation_AndAReplyStaysOnTheDonationOfItsParent()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var first = await h.AddDonationAsync();
            var second = await h.AddDonationAsync();
            var supplier = As(h.Supplier.UserId);
            var service = Comments(h);

            var noDonation = await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AddAsync(new CreateCommentDto { Content = "תגובה סתם" }, supplier));
            Assert.Equal("יש לבחור את התרומה שעליה התגובה.", noDonation.Message);

            var root = await service.AddAsync(new CreateCommentDto { Content = "יש גם לחם", DonationId = first.Id }, supplier);

            // תשובה שמנסה לעבור לתרומה אחרת - נדחית
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AddAsync(new CreateCommentDto { Content = "x", ParentCommentId = root.Id, DonationId = second.Id }, supplier));

            var reply = await service.AddAsync(new CreateCommentDto { Content = "מצוין", ParentCommentId = root.Id }, supplier);
            Assert.Equal(first.Id, reply.DonationId);
            h.Fresh();
            Assert.All(await h.Db.Comments.ToListAsync(), c => Assert.Equal(first.Id, c.DonationId));
        }

        [Fact]
        public async Task ForAUserWithTwoRoles_EachRolesPageShowsOnlyTheDonationsOfThatRole()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();

            // הספק הוא גם מתנדב (תיקון 10) - ומתנדב על תרומה של ספק אחר
            var otherSupplierUser = EngineHarness.NewUser("bakery2@test", UserRole.Supplier);
            h.Db.Users.Add(otherSupplierUser);
            await h.Db.SaveChangesAsync();
            var otherSupplier = new Supplier
            {
                UserId = otherSupplierUser.Id, BusinessName = "ירקות השדה", Address = "רחוב 2", Latitude = 32.08, Longitude = 34.78,
                TaxId = "987654321", GooglePlaceId = "N/A", BusinessLicenseFilePath = "y.pdf", ApprovalStatus = ApprovalStatus.Approved
            };
            h.Db.Suppliers.Add(otherSupplier);
            var myVolunteerProfile = new Volunteer
            {
                UserId = h.Supplier.UserId, VehicleType = "רכב פרטי", MaxCapacityKg = 100, IsAvailable = true,
                IdNumber = "000000018", DriverLicenseNumber = "1234567", DriverLicenseExpiry = h.NowUtc.AddYears(2),
                DateOfBirth = new DateTime(1990, 1, 1), ReliabilityScore = 3, ApprovalStatus = ApprovalStatus.Approved,
                RowVersion = new byte[8]
            };
            h.Db.Volunteers.Add(myVolunteerProfile);
            await h.Db.SaveChangesAsync();

            var myDonation = await h.AddDonationAsync();
            var pickedUpDonation = await h.AddDonationAsync();
            pickedUpDonation.SupplierId = otherSupplier.Id;
            await h.Db.SaveChangesAsync();
            await AddMatchAsync(h, myVolunteerProfile, pickedUpDonation, MatchStatus.InPickup);

            var me = As(h.Supplier.UserId);
            var service = Comments(h);
            await service.AddAsync(new CreateCommentDto { Content = "על התרומה שפרסמתי", DonationId = myDonation.Id }, me);
            await service.AddAsync(new CreateCommentDto { Content = "על התרומה שאני אוסף", DonationId = pickedUpDonation.Id }, me);
            h.Fresh();

            Assert.Equal(myDonation.Id, Assert.Single(await service.GetTreeAsync(me, RequestedRole.Supplier)).DonationId);
            Assert.Equal(pickedUpDonation.Id, Assert.Single(await service.GetTreeAsync(me, RequestedRole.Volunteer)).DonationId);
            Assert.Equal(2, (await service.GetTreeAsync(me)).Count);
        }
    }
}
