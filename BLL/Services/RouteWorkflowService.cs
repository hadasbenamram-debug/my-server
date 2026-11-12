using BLL.Algorithms;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Notifications;
using BLL.Security;
using DAL.Classes;
using DAL.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    /// <summary>
    /// מחזור החיים של מסלול ושל השידוכים שבו (מסמך התיאור, שלבים 6-10):
    ///   ממתין לאישור -> אושר (המתנדב Busy) -> יצא לדרך (InPickup) -> נאסף (InTransit) -> נמסר (Completed)
    ///   -> העמותה מאשרת קבלה (התרומה Completed). דחייה / אי-מענה / No-Show -> שיבוץ מחדש מיידי.
    ///
    /// תוקנו כאן: אישור ודחייה ברמת המסלול (בעבר כל תחנה בנפרד), דחייה והשלמה שלא עדכנו את התרומה
    /// (match.Donation לא נטען) ולא כיבו את IsActive, והסטטוסים PickedUp/InPickup/InTransit/NoShow/Completed
    /// שלא נקבעו אף פעם.
    ///
    /// כל שלב שהצליח נרשם גם ביומן המשלוח (DeliveryLogs) ונשלח בזמן אמת למי שצופה במסלול (DeliveryTracker).
    /// </summary>
    public class RouteWorkflowService
    {
        private static readonly MatchStatus[] ActiveStatuses =
        {
            MatchStatus.AwaitingVolunteerConfirmation, MatchStatus.Confirmed, MatchStatus.InPickup, MatchStatus.InTransit
        };

        private readonly IMatchRepository _matchRepository;
        private readonly IVolunteerRouteRepository _routeRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IActionTokenService _actionTokens;
        private readonly INotificationService _notifications;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IBackgroundJobClient _jobs;
        private readonly VolunteerReliabilityService _reliability;
        private readonly ILogger<RouteWorkflowService> _logger;
        private readonly DeliveryTracker _tracker;
        private readonly TimeProvider _clock;   // שעון אחד לכל המערכת (בבדיקות - שעון קבוע)

        public RouteWorkflowService(
            IMatchRepository matchRepository,
            IVolunteerRouteRepository routeRepository,
            IVolunteerRepository volunteerRepository,
            IActionTokenService actionTokens,
            INotificationService notifications,
            IUnitOfWork unitOfWork,
            IBackgroundJobClient jobs,
            VolunteerReliabilityService reliability,
            ILogger<RouteWorkflowService> logger,
            DeliveryTracker tracker,
            TimeProvider? clock = null)
        {
            _matchRepository = matchRepository;
            _routeRepository = routeRepository;
            _volunteerRepository = volunteerRepository;
            _actionTokens = actionTokens;
            _notifications = notifications;
            _unitOfWork = unitOfWork;
            _jobs = jobs;
            _reliability = reliability;
            _logger = logger;
            _tracker = tracker;
            _clock = clock ?? TimeProvider.System;
        }

        public static bool IsActiveStatus(MatchStatus status) => ActiveStatuses.Contains(status);

        // כל השידוכים במסלול של השידוך הזה (או השידוך עצמו אם אין מסלול)
        public async Task<List<Match>> GetGroupAsync(Match match) =>
            match.VolunteerRouteId.HasValue
                ? await _matchRepository.GetByRouteIdAsync(match.VolunteerRouteId.Value)
                : new List<Match> { match };

        // =========================================================
        // אישור / דחייה / אי-מענה - תמיד לכל המסלול יחד
        // =========================================================

        // תיקון 12: המקרים הצפויים (הבקשה כבר לא ממתינה, זמן התגובה עבר, רישיון שפג, מסלול פעיל אחר)
        // חוזרים כתוצאה ולא כחריגה - בעבר מתנדב שנכנס אחרי 5 הדקות "הפיל" את השרת בהרצה מ-Visual Studio.
        public async Task<RouteResponseResult> AcceptAsync(Match match, int volunteerId)
        {
            var group = (await GetGroupAsync(match))
                .Where(m => m.VolunteerId == volunteerId && m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation)
                .ToList();

            if (group.Count == 0)
                return RouteResponseResult.NotAwaiting;

            var now = _clock.GetUtcNow().UtcDateTime;
            // תיקון 38: ברגע שהזמן הגיע (ולא רק אחריו) - כבר מאוחר. השרת קובע לפי השעון שלו, גם אם הדף אצל המתנדב
            // לא רוענן והכפתור עוד מוצג.
            if (IsPastDeadline(group, now))
            {
                // תיקון 13: הזמן עבר - אין אישור מאוחר. אם הג'וב שמסמן "לא ענה בזמן" עוד לא הספיק לרוץ,
                // מסמנים כאן TimedOut והבקשה עוברת מיד למתנדב הבא (לא מחכים לג'וב)
                await DeclineAsync(match, volunteerId, MatchStatus.TimedOut);
                return RouteResponseResult.Expired;
            }

            // בדיקה חוזרת של רישיון הנהיגה ברגע האישור (המנוע בדק אותו כשבחר את המתנדב, אבל הרישיון
            // יכול לפוג בין ההצעה לאישור - למשל בחצות). רישיון לא בתוקף: הבקשה עוברת מיד למתנדב הבא.
            var volunteerProfile = group[0].Volunteer ?? await _volunteerRepository.GetByIdAsync(volunteerId);
            if (volunteerProfile != null && !DriverLicensePolicy.IsValid(volunteerProfile.DriverLicenseExpiry, now))
            {
                await DeclineAsync(match, volunteerId, MatchStatus.Declined);
                return RouteResponseResult.LicenseInvalid;
            }

            if (await _matchRepository.HasActiveConfirmedMatchAsync(volunteerId, match.Id, match.VolunteerRouteId))
                return RouteResponseResult.VolunteerBusy;

            // תיקון 14: אם בדיוק באותו רגע הבקשה סומנה "לא ענה בזמן" (או נענתה במכשיר אחר) - העדכון שלנו נכשל
            // (הסטטוס הוא Concurrency Token) והאישור לא נשמר. אין מצב של שני מתנדבים לאותה תרומה.
            bool lateAtSave = false;
            bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                // תיקון 38: בדיקה חוזרת ממש לפני השמירה - אישור שהתחיל ב-4:59.9 ונשמר אחרי 5:00 לא נקלט
                now = _clock.GetUtcNow().UtcDateTime;
                if (IsPastDeadline(group, now))
                {
                    lateAtSave = true;
                    return;
                }

                foreach (var m in group)
                {
                    m.Status = MatchStatus.Confirmed;
                    m.RespondedAt = now;
                }

                // קודם הסטטוס (זה העדכון שנבדק מול שינוי במקביל), ורק אחריו שאר השינויים
                await _matchRepository.UpdateAsync(group[0]);

                // המתנדב מסומן "תפוס" עד סיום המסלול
                var volunteer = group[0].Volunteer ?? await _volunteerRepository.GetByIdAsync(volunteerId);
                if (volunteer != null)
                {
                    volunteer.IsAvailable = false;
                    await _volunteerRepository.UpdateAsync(volunteer);
                }

                await RevokeLinkTokensAsync(group[0]);
            });

            if (lateAtSave)
            {
                await DeclineAsync(match, volunteerId, MatchStatus.TimedOut);
                return RouteResponseResult.Expired;
            }

            if (!saved)
                return RouteResponseResult.NotAwaiting;

            await _tracker.RecordEventAsync(match.VolunteerRouteId, DeliveryLogType.Accepted, "המתנדב אישר את המסלול");
            await NotifyParticipantsAsync(group, "המתנדב אישר את המסלול", $"המתנדב יגיע לאיסוף בסביבות {EtaText(group)}.");
            await PublishStatusAsync(group);
            return RouteResponseResult.Done;
        }

        // תיקון 38: חלון התגובה הסתיים (deadline כולל: ב-5:00 בדיוק כבר מאוחר)
        private static bool IsPastDeadline(IEnumerable<Match> group, DateTime nowUtc) =>
            group.Any(m => m.ResponseDeadline.HasValue && m.ResponseDeadline.Value <= nowUtc);

        // דחייה של המתנדב (Declined) או אי-מענה בזמן (TimedOut). NotAwaiting = הבקשה כבר לא ממתינה (בלי חריגה).
        public async Task<RouteResponseResult> DeclineAsync(Match match, int volunteerId, MatchStatus outcome = MatchStatus.Declined)
        {
            if (outcome != MatchStatus.Declined && outcome != MatchStatus.TimedOut)
                throw new ArgumentOutOfRangeException(nameof(outcome));

            var all = await GetGroupAsync(match);
            var group = all
                .Where(m => m.VolunteerId == volunteerId && m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation)
                .ToList();

            if (group.Count == 0)
                return RouteResponseResult.NotAwaiting;

            var now = _clock.GetUtcNow().UtcDateTime;

            // תיקון 14: אם בדיוק באותו רגע המתנדב אישר (או שהבקשה כבר טופלה בתהליך אחר) - לא דורסים,
            // ולא שולחים את התרומה למתנדב נוסף
            bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                foreach (var m in group)
                {
                    m.Status = outcome;
                    m.IsActive = false;
                    if (outcome == MatchStatus.Declined)
                        m.RespondedAt = now;
                }

                // קודם הסטטוס (זה העדכון שנבדק מול שינוי במקביל), ורק אחריו שאר השינויים
                await _matchRepository.UpdateAsync(group[0]);

                // התרומה חוזרת למאגר הפתוחות
                foreach (var m in group.Where(m => m.Donation != null && m.Donation.Status == DonationStatus.Matched))
                    m.Donation.Status = DonationStatus.Pending;

                await _matchRepository.UpdateAsync(group[0]);
                await CloseRouteIfDoneAsync(group[0], all, releaseVolunteer: false);
                await RevokeLinkTokensAsync(group[0]);
            });

            if (!saved)
                return RouteResponseResult.NotAwaiting;

            if (outcome == MatchStatus.TimedOut && group[0].Volunteer != null)
            {
                await _notifications.NotifyUserAsync(group[0].Volunteer.UserId,
                    "פג זמן התגובה לבקשת ההתנדבות", "הבקשה הועברה למתנדב אחר.", type: "warning");
            }

            await PublishStatusAsync(group);

            // מסמך התיאור, שלב 8: הבקשה עוברת אוטומטית למתנדב הבא - בלי לחכות לריצה השעתית
            foreach (var donationId in group.Select(m => m.DonationId).Distinct())
                ScheduleReassign(donationId);

            return RouteResponseResult.Done;
        }

        // =========================================================
        // ביצוע המסלול
        // =========================================================

        public async Task<List<Match>> StartRouteAsync(int routeId, int volunteerId)
        {
            // רק השידוכים הפעילים של המתנדב הזה במסלול (מסלול של מתנדב אחר = "לא נמצא")
            var matches = await _matchRepository.GetActiveByRouteForVolunteerAsync(routeId, volunteerId);

            if (matches.Count == 0)
                throw new NotFoundException("המסלול לא נמצא.");

            if (matches.Any(m => m.Status == MatchStatus.AwaitingVolunteerConfirmation))
                throw new ArgumentException("יש לאשר את המסלול לפני היציאה לדרך.");

            var now = _clock.GetUtcNow().UtcDateTime;
            bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                foreach (var m in matches.Where(m => m.Status == MatchStatus.Confirmed))
                    m.Status = MatchStatus.InPickup;

                var route = await _routeRepository.GetByIdAsync(routeId);
                if (route != null && route.Status == RouteStatus.Planned)
                {
                    route.Status = RouteStatus.InProgress;
                    route.StartedAt ??= now;
                }

                await _matchRepository.UpdateAsync(matches[0]);
            });
            EnsureSaved(saved);

            // בלי מיקום: נקודת היציאה של המתנדב (למשל הבית שלו) לא נחשפת. המעקב מתחיל מעדכון המיקום הבא.
            await _tracker.RecordEventAsync(routeId, DeliveryLogType.RouteStarted, "המתנדב יצא לדרך");
            await NotifyParticipantsAsync(matches, "המתנדב יצא לדרך", "אפשר לעקוב אחרי המיקום שלו בזמן אמת.");
            await PublishStatusAsync(matches);
            return matches;
        }

        public async Task PickUpAsync(Match match, int volunteerId)
        {
            if (match.VolunteerId != volunteerId)
                throw new NotFoundException("השידוך לא נמצא.");

            if (!match.IsActive || (match.Status != MatchStatus.Confirmed && match.Status != MatchStatus.InPickup))
                throw new ArgumentException("אפשר לסמן איסוף רק לשיבוץ שאושר ועדיין לא נאסף.");

            var now = _clock.GetUtcNow().UtcDateTime;
            bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                match.Status = MatchStatus.InTransit;
                match.PickedUpAt = now;

                if (match.Donation != null)
                    match.Donation.Status = DonationStatus.PickedUp;

                if (match.VolunteerRouteId.HasValue)
                {
                    var route = await _routeRepository.GetByIdAsync(match.VolunteerRouteId.Value);
                    if (route != null && route.Status == RouteStatus.Planned)
                    {
                        route.Status = RouteStatus.InProgress;
                        route.StartedAt ??= now;
                    }
                }

                await _matchRepository.UpdateAsync(match);
            });
            EnsureSaved(saved);

            var supplierName = match.Donation?.Supplier?.BusinessName;
            await _tracker.RecordEventAsync(match.VolunteerRouteId, DeliveryLogType.PickedUp,
                $"תרומה #{match.DonationId} נאספה" + (string.IsNullOrWhiteSpace(supplierName) ? "" : $" מ{supplierName}"),
                match.Id, match.Volunteer);
            await NotifyParticipantsAsync(new[] { match }, $"תרומה #{match.DonationId} נאספה", "המתנדב בדרך לעמותה.");
            await PublishStatusAsync(new[] { match });
        }

        // המתנדב (או מנהל) מסמן שהתרומה נמסרה לעמותה
        public async Task DeliverAsync(Match match)
        {
            if (match.Status != MatchStatus.Confirmed && match.Status != MatchStatus.InPickup && match.Status != MatchStatus.InTransit)
                throw new ArgumentException("לא ניתן לסמן מסירה לשיבוץ במצב הנוכחי.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var group = await GetGroupAsync(match);

            bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                match.PickedUpAt ??= now;
                match.DeliveredAt = now;
                match.Status = MatchStatus.Completed;
                match.IsActive = false;

                if (match.Donation != null &&
                    (match.Donation.Status == DonationStatus.Matched || match.Donation.Status == DonationStatus.PickedUp))
                    match.Donation.Status = DonationStatus.Delivered;

                await _matchRepository.UpdateAsync(match);
                await CloseRouteIfDoneAsync(match, group, releaseVolunteer: true);
            });
            EnsureSaved(saved);

            await _tracker.RecordEventAsync(match.VolunteerRouteId, DeliveryLogType.Delivered,
                $"תרומה #{match.DonationId} נמסרה" + (match.Ngo == null ? " לעמותה" : $" ל{match.Ngo.NgoName}"),
                match.Id, match.Volunteer);

            // תיקון 63: ההתראות מובילות למסך התרומה (הדף "אישור קבלה" נמחק - העמותה מאשרת שם)
            if (match.Ngo != null)
            {
                await _notifications.NotifyUserAsync(match.Ngo.UserId,
                    $"תרומה #{match.DonationId} נמסרה",
                    "נא לאשר את קבלת התרומה.",
                    link: FrontendLinks.NgoDonationPath(match.DonationId), type: "request");
            }

            if (match.Donation?.Supplier != null)
                await _notifications.NotifyUserAsync(match.Donation.Supplier.UserId, $"תרומה #{match.DonationId} נמסרה לעמותה", null,
                    link: FrontendLinks.SupplierDonationPath(match.DonationId), type: "success");

            await PublishStatusAsync(new[] { match });
        }

        // העמותה מאשרת שקיבלה את התרומה (חדש - לא היה שלב כזה)
        public async Task<Match> ConfirmReceiptAsync(int donationId, int ngoId)
        {
            var match = await _matchRepository.GetCurrentForDonationAsync(donationId);
            if (match == null || match.NgoId != ngoId || match.Donation == null)
                throw new NotFoundException("התרומה לא נמצאה.");

            var donation = match.Donation;
            if (donation.Status == DonationStatus.Completed)
                return match;

            bool pickedUp = donation.Status == DonationStatus.PickedUp ||
                            donation.Status == DonationStatus.Delivered ||
                            match.Status == MatchStatus.InTransit;
            if (!pickedUp)
                throw new ArgumentException("אפשר לאשר קבלה רק לתרומה שנאספה.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var group = await GetGroupAsync(match);

            bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (IsActiveStatus(match.Status))
                {
                    match.PickedUpAt ??= now;
                    match.DeliveredAt ??= now;
                    match.Status = MatchStatus.Completed;
                    match.IsActive = false;
                }

                donation.Status = DonationStatus.Completed;
                donation.ReceivedAt = now;

                await _matchRepository.UpdateAsync(match);
                await CloseRouteIfDoneAsync(match, group, releaseVolunteer: true);
            });
            EnsureSaved(saved);

            await _tracker.RecordEventAsync(match.VolunteerRouteId, DeliveryLogType.ReceiptConfirmed,
                $"העמותה אישרה את קבלת תרומה #{donation.Id}", match.Id);

            if (donation.Supplier != null)
                await _notifications.NotifyUserAsync(donation.Supplier.UserId, $"העמותה אישרה את קבלת תרומה #{donation.Id}", "תודה על התרומה!", type: "success");

            if (match.Volunteer != null)
                await _notifications.NotifyUserAsync(match.Volunteer.UserId, $"העמותה אישרה את קבלת תרומה #{donation.Id}", "תודה על ההתנדבות!", type: "success");

            await PublishStatusAsync(new[] { match });
            return match;
        }

        // =========================================================
        // No-Show
        // =========================================================

        // תיקון 36: autoReassign=false - המנהל מטפל בתרומות בעצמו (שיבוץ ידני למתנדב שהוא בוחר), ולכן לא מתזמנים
        // שיבוץ אוטומטי מיידי. התרומות חוזרות ל-Pending, כך שאם המנהל לא ישבץ - הריצה הבאה של המנוע תטפל בהן.
        public async Task<int> MarkNoShowAsync(Match match, string? reason, bool automatic, bool autoReassign = true)
        {
            var all = await GetGroupAsync(match);
            var group = all
                .Where(m => m.IsActive && m.PickedUpAt == null &&
                            (m.Status == MatchStatus.Confirmed || m.Status == MatchStatus.InPickup))
                .ToList();

            if (group.Count == 0)
                throw new ArgumentException("אפשר לסמן No-Show רק לשיבוץ שאושר ועדיין לא נאסף.");

            int volunteerId = group[0].VolunteerId;

            bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                foreach (var m in group)
                {
                    m.Status = MatchStatus.NoShow;
                    m.IsActive = false;
                    if (m.Donation != null && m.Donation.Status == DonationStatus.Matched)
                        m.Donation.Status = DonationStatus.Pending;
                }

                await _matchRepository.UpdateAsync(group[0]);
                await CloseRouteIfDoneAsync(group[0], all, releaseVolunteer: true);
            });
            EnsureSaved(saved);

            await _tracker.RecordEventAsync(group[0].VolunteerRouteId, DeliveryLogType.Cancelled,
                "המתנדב לא הגיע לאיסוף" + (automatic ? " (זוהה אוטומטית)" : "") +
                (autoReassign ? ". התרומות הועברו לשיבוץ מחדש." : ". מנהל המערכת משבץ את התרומות מחדש."));

            // ציון האמינות יורד (MatchingPolicy:NoShowReliabilityPenalty)
            await _reliability.RecalculateAsync(volunteerId);

            var volunteerUserId = group[0].Volunteer?.UserId;
            if (volunteerUserId.HasValue)
            {
                await _notifications.NotifyUserAsync(volunteerUserId.Value,
                    "השיבוץ סומן כ-No-Show",
                    "התרומות הועברו למתנדב אחר." + (string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})"),
                    type: "warning");
            }

            await _notifications.NotifyAdminsAsync(
                $"No-Show במסלול #{group[0].VolunteerRouteId ?? group[0].Id}",
                (automatic ? "זוהה אוטומטית. " : "") +
                (autoReassign ? "התרומות הועברו לשיבוץ מחדש מיידי." : "התרומות ממתינות לשיבוץ ידני בעמוד \"טיפול באי-הגעה\"."),
                link: "/app/admin/noshow", type: "warning");

            await PublishStatusAsync(group);

            // מסמך התיאור, שלב 9: שיבוץ מחדש מיידי, בלי לחכות לריצה השעתית
            if (autoReassign)
            {
                foreach (var donationId in group.Select(m => m.DonationId).Distinct())
                    ScheduleReassign(donationId);
            }

            return group.Count;
        }

        // =========================================================
        // ביטול שידוך בגלל ביטול / תפוגה של התרומה (נקרא בתוך טרנזקציה של הקורא)
        // =========================================================

        public async Task<Match?> CancelActiveMatchForDonationAsync(Donation donation, string reason)
        {
            var match = await _matchRepository.GetCurrentForDonationAsync(donation.Id);
            if (match == null || !match.IsActive || !IsActiveStatus(match.Status))
                return null;

            if (match.Status == MatchStatus.InTransit || match.PickedUpAt != null)
                throw new ConflictException("לא ניתן לבטל - התרומה כבר נאספה.");

            match.Status = MatchStatus.Cancelled;
            match.IsActive = false;
            await _matchRepository.UpdateAsync(match);

            var group = await GetGroupAsync(match);
            await CloseRouteIfDoneAsync(match, group, releaseVolunteer: true);
            await RevokeLinkTokensAsync(match);

            if (match.Volunteer != null)
            {
                await _notifications.NotifyUserAsync(match.Volunteer.UserId,
                    $"תרומה #{donation.Id} בוטלה", reason, type: "warning");
            }

            return match;
        }

        // =========================================================
        // עזר
        // =========================================================

        // תיקון 14: השידוך השתנה בדיוק באותו רגע בתהליך אחר (הסטטוס הוא Concurrency Token) - שום דבר לא נשמר.
        // בעבר הפעולה "הצליחה" בלי שנשמר כלום; עכשיו המשתמש מתבקש לרענן ולנסות שוב.
        private static void EnsureSaved(bool saved)
        {
            if (!saved)
                throw new ConflictException("המצב של השיבוץ השתנה בדיוק עכשיו. נא לרענן את הדף ולנסות שוב.");
        }

        // כשאין יותר שידוכים פעילים במסלול - סוגרים אותו, והמתנדב חוזר להיות זמין
        private async Task CloseRouteIfDoneAsync(Match match, List<Match> group, bool releaseVolunteer)
        {
            if (group.Any(m => m.IsActive && IsActiveStatus(m.Status)))
                return;

            if (match.VolunteerRouteId.HasValue)
            {
                var route = match.VolunteerRoute ?? await _routeRepository.GetByIdAsync(match.VolunteerRouteId.Value);
                if (route != null && route.Status != RouteStatus.Completed && route.Status != RouteStatus.Cancelled)
                {
                    route.Status = group.Any(m => m.Status == MatchStatus.Completed) ? RouteStatus.Completed : RouteStatus.Cancelled;
                    route.CompletedAt = _clock.GetUtcNow().UtcDateTime;
                    await _routeRepository.UpdateAsync(route);
                }
            }

            if (releaseVolunteer)
            {
                var volunteer = match.Volunteer ?? await _volunteerRepository.GetByIdAsync(match.VolunteerId);
                if (volunteer != null && !volunteer.IsAvailable)
                {
                    volunteer.IsAvailable = true;      // מסמך התיאור, שלב 10: חוזר לסטטוס Available
                    await _volunteerRepository.UpdateAsync(volunteer);
                }
            }
        }

        private Task RevokeLinkTokensAsync(Match match) =>
            match.VolunteerRouteId.HasValue
                ? _actionTokens.RevokeAsync(null, ActionTokenPurpose.MatchResponse, match.VolunteerRouteId.Value)
                : Task.CompletedTask;

        private void ScheduleReassign(int donationId)
        {
            try
            {
                _jobs.Enqueue<DonationMatchingBatchService>(s => s.ReassignDonationAsync(donationId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not enqueue re-assignment of donation {DonationId}; the hourly run will pick it up.", donationId);
            }
        }

        // ההתראה לספקים ולעמותה מקשרת למסך המעקב החי של המסלול
        private async Task NotifyParticipantsAsync(IEnumerable<Match> matches, string subject, string? content)
        {
            var list = matches.ToList();
            var supplierUsers = list.Where(m => m.Donation?.Supplier != null).Select(m => m.Donation.Supplier.UserId).Distinct();
            var ngoUsers = list.Where(m => m.Ngo != null).Select(m => m.Ngo.UserId).Distinct();

            var routeIds = list.Select(m => m.VolunteerRouteId).Distinct().ToList();
            var link = routeIds.Count == 1 && routeIds[0].HasValue ? FrontendLinks.TrackingPath(routeIds[0]!.Value) : null;

            foreach (var userId in supplierUsers.Concat(ngoUsers).Distinct())
                await _notifications.NotifyUserAsync(userId, subject, content, link, type: "info");
        }

        private async Task PublishStatusAsync(IEnumerable<Match> matches)
        {
            foreach (var m in matches)
            {
                var payload = new { matchId = m.Id, donationId = m.DonationId, routeId = m.VolunteerRouteId, status = m.Status.ToString() };
                if (m.VolunteerRouteId.HasValue)
                    await _notifications.PublishAsync(RealtimeGroups.Route(m.VolunteerRouteId.Value), RealtimeEvents.MatchStatusChanged, payload);
                await _notifications.PublishAsync(RealtimeGroups.Admins, RealtimeEvents.MatchStatusChanged, payload);
            }
        }

        private string EtaText(IEnumerable<Match> matches)
        {
            var first = matches.Where(m => m.EstimatedArrival.HasValue).Select(m => m.EstimatedArrival!.Value).DefaultIfEmpty(_clock.GetUtcNow().UtcDateTime).Min();
            return LocalClock.ToLocal(first).ToString("HH:mm");
        }
    }
}
