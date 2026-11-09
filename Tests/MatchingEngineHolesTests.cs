using BLL.Algorithms;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ApprovalStatus = DAL.Classes.ApprovalStatus;

namespace FoodRescue.Tests
{
    /// <summary>
    /// תיקונים 19-28: כל בדיקה כאן מתארת "חור" שנמצא במנוע השיבוץ. כולן נכשלו על הקוד שלפני התיקון
    /// (הורצו מולו), ועוברות אחריו. השעון הקבוע של EngineHarness: יום שני 19/10/2026 10:00 שעון ישראל.
    /// </summary>
    public class MatchingEngineHolesTests
    {
        private static DateTime LocalToUtc(int hour, int minute) => LocalClock.ToUtc(new DateTime(2026, 10, 19, hour, minute, 0));

        private static async Task<Ngo> AddNgoAsync(EngineHarness h, string email, double lat, double lon, int dailyCapacityKg = 100)
        {
            var user = EngineHarness.NewUser(email, UserRole.Ngo);
            h.Db.Users.Add(user);
            await h.Db.SaveChangesAsync();
            var ngo = h.NewNgo(user.Id, ApprovalStatus.Approved, "עמותה " + email);
            ngo.Latitude = lat;
            ngo.Longitude = lon;
            ngo.DailyCapacity = dailyCapacityKg;
            h.Db.Ngos.Add(ngo);
            await h.Db.SaveChangesAsync();
            return ngo;
        }

        // ---------------- תיקון 19: שיבוץ גלובלי (Branch & Bound) במקום חמדני ----------------

        // החמדן נתן את התרומה הדחופה (5 ק"ג) למתנדב הכי קרוב - שהוא גם היחיד שיכול להעמיס את התרומה הגדולה (40 ק"ג).
        // התוצאה: תרומה אחת שובצה ואחת נזרקה, למרות שיש שיבוץ שמציל את שתיהן.
        [Fact]
        public async Task GlobalAssignment_DoesNotWasteTheOnlyVolunteerWhoCanCarryTheBigDonation()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var urgentSmall = await h.AddDonationAsync(weightKg: 5, latitude: 32.080, longitude: 34.780, pickupIn: TimeSpan.FromHours(1));
            var bigFar = await h.AddDonationAsync(weightKg: 40, latitude: 32.188, longitude: 34.780, pickupIn: TimeSpan.FromHours(3));
            var strong = await h.AddVolunteerAsync("strong@test", capacityKg: 50);   // ליד התרומה הדחופה
            var small = await h.AddVolunteerAsync("small@test", capacityKg: 10);
            small.CurrentLatitude = 32.100;
            small.CurrentLongitude = 34.780;                                           // כ-2 ק"מ ממנה
            await h.Db.SaveChangesAsync();

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(2, result.MatchedCount);
            h.Fresh();
            var matches = await h.Db.Matches.ToListAsync();
            Assert.Contains(matches, m => m.DonationId == bigFar.Id && m.VolunteerId == strong.Id);
            Assert.Contains(matches, m => m.DonationId == urgentSmall.Id && m.VolunteerId == small.Id);
        }

        // ---------------- תיקון 20: העמותה נבחרת יחד עם המסלול, עם חלופות ----------------

        // העמותה הקרובה נסגרת ב-10:20. לפי הערכת נסיעה ישירה (בלי הדרך של המתנדב לתרומה) היא "מתאימה",
        // אז נבחרה מראש - והמסלול בפועל מגיע אליה אחרי הסגירה. לא נוסתה עמותה אחרת והתרומה הוסלמה למנהל.
        [Fact]
        public async Task NgoChoice_FallsBackToAnotherNgo_WhenTheNearestClosesBeforeTheVolunteerCanArrive()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            h.Ngo.SunToThuCloseTime = new TimeSpan(10, 20, 0);
            await h.Db.SaveChangesAsync();
            var open = await AddNgoAsync(h, "open@test", 32.100, 34.800);
            var v = await h.AddVolunteerAsync("vol@test");
            v.CurrentLatitude = 32.150;
            v.CurrentLongitude = 34.780;                                               // כ-8 ק"מ מהתרומה
            await h.Db.SaveChangesAsync();
            await h.AddDonationAsync();

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(1, result.RoutesCreated);
            h.Fresh();
            Assert.Equal(open.Id, (await h.Db.Matches.SingleAsync()).NgoId);
        }

        // ---------------- תיקון 21: קיבולת העמותה בק"ג, כמגבלה קשה ----------------

        // בטופס ההרשמה (NgoRegisterPage) השדה הוא "קיבולת יומית (ק"ג)", אבל השרת השווה אותו למספר השידוכים.
        // עמותה של 10 ק"ג ביום קיבלה תרומה של 30 ק"ג.
        [Fact]
        public async Task NgoDailyCapacity_IsInKg_AndIsAHardLimit()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            h.Ngo.DailyCapacity = 10;
            await h.Db.SaveChangesAsync();
            var roomy = await AddNgoAsync(h, "roomy@test", 32.100, 34.800, dailyCapacityKg: 200);
            await h.AddVolunteerAsync("vol@test");
            await h.AddDonationAsync(weightKg: 30);

            await h.Engine.RunBatchAsync("Manual");

            h.Fresh();
            Assert.Equal(roomy.Id, (await h.Db.Matches.SingleAsync()).NgoId);
        }

        // ---------------- תיקון 22: בחירת התחנות לפי הזמן, לא רק לפי המשקל ----------------

        // המתנדב זמין עד 10:30. ה-Knapsack "מילא את הרכב" בתרומות נוספות במרחק 8 ק"מ, המסלול המלא חרג
        // מ-10:30 - וכל המסלול נפסל, כולל התרומה הדחופה שאפשר היה לאסוף לבד בתוך 15 דקות.
        [Fact]
        public async Task ExtraStopsThatDoNotFitTheVolunteersTime_DoNotCancelTheWholeRoute()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");
            v.AvailabilitySlots.Single().EndTime = new TimeSpan(10, 30, 0);
            await h.Db.SaveChangesAsync();
            var anchor = await h.AddDonationAsync(weightKg: 5, pickupIn: TimeSpan.FromMinutes(40));
            await h.AddDonationAsync(weightKg: 5, latitude: 32.150, longitude: 34.780);
            await h.AddDonationAsync(weightKg: 5, latitude: 32.010, longitude: 34.780);
            await h.AddDonationAsync(weightKg: 5, latitude: 32.080, longitude: 34.860);
            await h.AddDonationAsync(weightKg: 5, latitude: 32.080, longitude: 34.700);

            await h.Engine.RunBatchAsync("Manual");

            h.Fresh();
            Assert.Contains(await h.Db.Matches.ToListAsync(), m => m.DonationId == anchor.Id);
        }

        // ---------------- תיקון 23: סדר התחנות האופטימלי (DP) במקום "השכן הקרוב" ----------------

        // "השכן הקרוב" נוסע קודם לתחנה הקרובה (X) ומאחר לתרומה הדחופה (Y). הסדר Y ואז X עומד בכל הזמנים.
        [Fact]
        public async Task StopOrder_IsOptimal_NearestNeighbourMissedTheUrgentDeadline()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("vol@test");                                              // 32.081, 34.781
            var urgentFar = await h.AddDonationAsync(weightKg: 5, latitude: 32.1530, longitude: 34.781, pickupIn: TimeSpan.FromMinutes(22));
            var nearLater = await h.AddDonationAsync(weightKg: 5, latitude: 32.081, longitude: 34.8022, pickupIn: TimeSpan.FromHours(3));

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(1, result.RoutesCreated);
            h.Fresh();
            var matches = await h.Db.Matches.OrderBy(m => m.EstimatedArrival).ToListAsync();
            Assert.Equal(new[] { urgentFar.Id, nearLater.Id }, matches.Select(m => m.DonationId).ToArray());
        }

        // ---------------- תיקון 24: מערכת השעות של המתנדב - גם משמרת שמתחילה בקרוב ----------------

        // המשמרת מתחילה ב-10:30 והאיסוף עד 11:00. בריצה של 10:00 המתנדב "לא זמין עכשיו", ובריצה של 11:00
        // כבר מאוחר מדי - התרומה לא שובצה אף פעם.
        [Fact]
        public async Task VolunteerWhoseShiftStartsSoon_IsPlannedForTheStartOfTheShift()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");
            var slot = v.AvailabilitySlots.Single();
            slot.StartTime = new TimeSpan(10, 30, 0);
            slot.EndTime = new TimeSpan(13, 0, 0);
            await h.Db.SaveChangesAsync();
            await h.AddDonationAsync(pickupIn: TimeSpan.FromHours(1));

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(1, result.RoutesCreated);
            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            Assert.True(match.EstimatedArrival >= LocalToUtc(10, 30));
            Assert.True(match.EstimatedArrival <= LocalToUtc(11, 0));
        }

        // ---------------- תיקון 25: אין התראת שווא למנהל ----------------

        // יש מתנדב שזמין בשעה 14:00 והאיסוף עד 17:00 - אין סיבה "להקפיץ" מנהל ב-10:00.
        [Fact]
        public async Task NoAdminAlert_WhenAVolunteerIsScheduledLaterToday_BeforeThePickupDeadline()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            var v = await h.AddVolunteerAsync("vol@test");
            var slot = v.AvailabilitySlots.Single();
            slot.StartTime = new TimeSpan(14, 0, 0);
            slot.EndTime = new TimeSpan(16, 0, 0);
            await h.Db.SaveChangesAsync();
            await h.AddDonationAsync(pickupIn: TimeSpan.FromHours(7));

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(0, result.RoutesCreated);
            h.Fresh();
            Assert.Empty(await h.Db.Notifications.Where(n => n.UserId == h.AdminUser.Id).ToListAsync());
        }

        // ---------------- תיקון 26: עמותה שנפתחת בקרוב - דוחים את היציאה במקום להיכשל ----------------

        [Fact]
        public async Task NgoThatOpensSoon_TheDepartureIsDelayed_InsteadOfFailing()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            h.Ngo.SunToThuOpenTime = new TimeSpan(10, 45, 0);
            await h.Db.SaveChangesAsync();
            await h.AddVolunteerAsync("vol@test");
            await h.AddDonationAsync(pickupIn: TimeSpan.FromMinutes(75));

            var result = await h.Engine.RunBatchAsync("Manual");

            Assert.Equal(1, result.RoutesCreated);
            h.Fresh();
            var route = await h.Db.VolunteerRoutes.SingleAsync();
            var ngoArrival = route.ScheduledAt.AddMinutes(route.EstimatedTotalDurationMinutes ?? 0);
            Assert.True(ngoArrival >= LocalToUtc(10, 45));
        }

        // ---------------- תיקון 27: זמן התגובה של המתנדב נכלל בזמני ההגעה ----------------

        // ההצעה נשלחת ב-10:00 והמתנדב יכול לאשר עד 10:05. ה-ETA חושב כאילו הוא כבר יצא ב-10:00.
        [Fact]
        public async Task Eta_LeavesTimeForTheVolunteersResponse()
        {
            using var h = new EngineHarness();
            await h.SeedBasicsAsync();
            await h.AddVolunteerAsync("vol@test");
            await h.AddDonationAsync();

            await h.Engine.RunBatchAsync("Manual");

            h.Fresh();
            var match = await h.Db.Matches.SingleAsync();
            Assert.True(match.EstimatedArrival >= h.NowUtc.AddMinutes(h.Policy.Current.ResponseTimeoutMinutes));
        }

        // ---------------- תיקון 28: משבצת זמינות שחוצה חצות ----------------

        // משמרת של יום שני 22:00-02:00: ביום שלישי ב-01:00 המתנדב באמצע המשמרת של יום שני.
        // הקוד הקודם בדק רק משבצות של "היום" (שלישי) ולכן פספס אותה - ומנגד "פתח" את המשמרת של שלישי בלילה.
        [Fact]
        public void OvernightSlot_TheTailBelongsToThePreviousDay()
        {
            LocalClock.Configure("Asia/Jerusalem");
            var tuesday0100 = LocalClock.ToUtc(new DateTime(2026, 10, 20, 1, 0, 0));
            Volunteer With(DayOfWeek day) => new()
            {
                AvailabilitySlots = new List<AvailabilitySlot>
                {
                    new() { RegionId = 1, Day = day, StartTime = new TimeSpan(22, 0, 0), EndTime = new TimeSpan(2, 0, 0) }
                }
            };

            Assert.True(AvailabilityHelper.CanServeAt(With(DayOfWeek.Monday), tuesday0100, 1, 1));
            Assert.False(AvailabilityHelper.CanServeAt(With(DayOfWeek.Tuesday), tuesday0100, 1, 1));
        }

        // ---------------- תיקון 29: סינון מוקדם בלי "שלילות שווא" ----------------

        // הסינון המוקדם העריך זמן נסיעה ב-40 קמ"ש x 1.3 ופסל מתנדב לפני שנשאל Google. הערכה כזו אינה
        // חסם תחתון: בכביש מהיר המתנדב מגיע בזמן. סינון מוקדם מותר רק לפי חסם תחתון (ההחלטה - במסלול).
        [Fact]
        public void Prefilter_DoesNotDropAVolunteerThatAFastRoadCanBringInTime()
        {
            LocalClock.Configure("Asia/Jerusalem");
            var asOf = LocalClock.ToUtc(new DateTime(2026, 10, 19, 9, 0, 0));
            var donation = new Donation
            {
                Id = 1, CreatedAt = asOf, Latitude = 32.0, Longitude = 34.8, RegionId = 1,
                LatestPickupTime = new TimeSpan(9, 35, 0), ExpirationTime = asOf.AddHours(6),
                DonationFoodCategories = new List<DonationFoodCategory>()
            };
            var ngo = new Ngo { Id = 1, RegionId = 1, Latitude = 32.0, Longitude = 34.8, SunToThuOpenTime = TimeSpan.Zero, SunToThuCloseTime = new TimeSpan(23, 59, 0) };
            var volunteer = new Volunteer
            {
                Id = 7, IsAvailable = true, CurrentLatitude = 32.2, CurrentLongitude = 34.8, LocationUpdatedAt = asOf,   // 22 ק"מ צפונה
                AvailabilitySlots = new List<AvailabilitySlot>
                {
                    new() { RegionId = 1, Day = DayOfWeek.Monday, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 0) }
                }
            };

            var candidates = new RegionAvailabilityMatchingService().FindCandidates(donation, ngo, new[] { volunteer }, asOf);

            Assert.Contains(candidates, c => c.Id == volunteer.Id);
        }
    }
}
