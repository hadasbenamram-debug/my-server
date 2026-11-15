using DAL.Classes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 81: כשאין מתנדב לתרומה - המנוע ומסך השיבוץ הידני אומרים בדיוק למה
    public sealed class NoVolunteerExplanationTests : IDisposable
    {
        private readonly EngineHarness _h = new();

        public void Dispose() => _h.Dispose();

        private async Task<Region> AddRegionAsync(string name)
        {
            var region = new Region { RegionName = name, CenterLatitude = 32.79, CenterLongitude = 34.99, RadiusKm = 30 };
            _h.Db.Regions.Add(region);
            await _h.Db.SaveChangesAsync();
            return region;
        }

        [Fact]
        public async Task NgoInAnotherRegion_TheReasonNamesTheRegion_AndWhatToChange()
        {
            await _h.SeedBasicsAsync();
            var north = await AddRegionAsync("צפון");
            _h.Ngo.RegionId = north.Id;                 // העמותה היחידה - באזור אחר מהמשמרת של המתנדב
            await _h.Db.SaveChangesAsync();
            await _h.AddVolunteerAsync("vol@test");     // משמרת באזור "מרכז", בלי "נוסע/ת לאזור"
            await _h.AddDonationAsync();

            var result = await _h.Engine.RunBatchAsync("Manual");

            Assert.Equal(0, result.RoutesCreated);
            Assert.Contains(result.FailedConstraints, f =>
                f.Contains("vol:") && f.Contains("המשמרת שלו באזור מרכז") && f.Contains("נוסע/ת לאזור: צפון"));
        }

        [Fact]
        public async Task NgoAtItsDailyCapacity_IsNamed_InTheReason()
        {
            await _h.SeedBasicsAsync();
            var north = await AddRegionAsync("צפון");

            // העמותה באזור של המתנדב קטנה מדי לתרומה; העמותה השנייה - באזור אחר
            _h.Ngo.DailyCapacity = 3;
            var otherUser = EngineHarness.NewUser("ngo2@test", UserRole.Ngo);
            _h.Db.Users.Add(otherUser);
            await _h.Db.SaveChangesAsync();
            var other = _h.NewNgo(otherUser.Id, ApprovalStatus.Approved, "עמותת הצפון");
            other.RegionId = north.Id;
            _h.Db.Ngos.Add(other);
            await _h.Db.SaveChangesAsync();

            await _h.AddVolunteerAsync("vol@test");
            await _h.AddDonationAsync(weightKg: 5);

            var result = await _h.Engine.RunBatchAsync("Manual");

            Assert.Equal(0, result.RoutesCreated);
            var reason = Assert.Single(result.FailedConstraints, f => f.Contains("vol:"));
            Assert.Contains("עמותת בדיקה", reason);
            Assert.Contains("קיבולת היומית", reason);
            Assert.Contains("נוסע/ת לאזור: צפון", reason);
        }

        [Fact]
        public async Task BusyVolunteer_TheReasonSaysHeAlreadyHasARoute()
        {
            await _h.SeedBasicsAsync();
            var volunteer = await _h.AddVolunteerAsync("vol@test");

            // הצעה פתוחה על תרומה אחרת
            var earlier = await _h.AddDonationAsync();
            earlier.Status = DonationStatus.Matched;
            _h.Db.Matches.Add(new Match
            {
                DonationId = earlier.Id, NgoId = _h.Ngo.Id, VolunteerId = volunteer.Id,
                IsActive = true, Status = MatchStatus.Confirmed, AssignedAt = _h.NowUtc
            });
            await _h.Db.SaveChangesAsync();

            await _h.AddDonationAsync();

            var result = await _h.Engine.RunBatchAsync("Manual");

            Assert.Contains(result.FailedConstraints, f => f.Contains("vol: כבר יש לו הצעה פתוחה או מסלול פעיל"));
        }

        [Fact]
        public async Task ManualAssignDialog_ExplainsWhyTheShiftDoesNotFit()
        {
            await _h.SeedBasicsAsync();
            var north = await AddRegionAsync("צפון");
            _h.Ngo.RegionId = north.Id;
            await _h.Db.SaveChangesAsync();
            await _h.AddVolunteerAsync("vol@test");
            var donation = await _h.AddDonationAsync();

            var candidates = await _h.Engine.GetManualCandidatesAsync(donation.Id);

            var option = Assert.Single(candidates.Volunteers);
            Assert.False(option.AvailableBySchedule);
            Assert.Contains("נוסע/ת לאזור: צפון", option.Note);
            Assert.EndsWith("רק בתיאום איתו", option.Note);
        }
    }
}
