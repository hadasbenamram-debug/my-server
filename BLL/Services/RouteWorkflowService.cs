using BLL.Algorithms;
using BLL.Exceptions;
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

        public RouteWorkflowService(
            IMatchRepository matchRepository,
            IVolunteerRouteRepository routeRepository,
            IVolunteerRepository volunteerRepository,
            IActionTokenService actionTokens,
            INotificationService notifications,
            IUnitOfWork unitOfWork,
            IBackgroundJobClient jobs,
            VolunteerReliabilityService reliability,
            ILogger<RouteWorkflowService> logger)
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

        public async Task<List<Match>> AcceptAsync(Match match, int volunteerId)
        {
            var group = (await GetGroupAsync(match))
                .Where(m => m.VolunteerId == volunteerId && m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation)
                .ToList();

            if (group.Count == 0)
                throw new ArgumentException("לא ניתן לאשר: הבקשה אינה ממתינה לאישור או שפג תוקפה.");

            var now = DateTime.UtcNow;
            if (group.Any(m => m.ResponseDeadline.HasValue && m.ResponseDeadline.Value < now))
                throw new ArgumentException("פג זמן התגובה לבקשה זו.");

            if (await _matchRepository.HasActiveConfirmedMatchAsync(volunteerId, match.Id, match.VolunteerRouteId))
                throw new ConflictException("למתנדב כבר יש שיבוץ פעיל אחר.");

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                foreach (var m in group)
                {
                    m.Status = MatchStatus.Confirmed;
                    m.RespondedAt = now;
                }

                // המתנדב מסומן "תפוס" עד סיום המסלול
                var volunteer = group[0].Volunteer ?? await _volunteerRepository.GetByIdAsync(volunteerId);
                if (volunteer != null)
                    volunteer.IsAvailable = false;

                await _matchRepository.UpdateAsync(group[0]);
                await RevokeLinkTokensAsync(group[0]);
            });

            await NotifyParticipantsAsync(group, "המתנדב אישר את המסלול", $"המתנדב יגיע לאיסוף בסביבות {EtaText(group)}.");
            await PublishStatusAsync(group);
            return group;
        }

        public async Task DeclineAsync(Match match, int volunteerId, MatchStatus outcome = MatchStatus.Declined)
        {
            if (outcome != MatchStatus.Declined && outcome != MatchStatus.TimedOut)
                throw new ArgumentOutOfRangeException(nameof(outcome));

            var all = await GetGroupAsync(match);
            var group = all
                .Where(m => m.VolunteerId == volunteerId && m.IsActive && m.Status == MatchStatus.AwaitingVolunteerConfirmation)
                .ToList();

            if (group.Count == 0)
            {
                if (outcome == MatchStatus.Declined)
                    throw new ArgumentException("לא ניתן לדחות: הבקשה אינה ממתינה לתגובה.");
                return;
            }

            var now = DateTime.UtcNow;

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                foreach (var m in group)
                {
                    m.Status = outcome;
                    m.IsActive = false;
                    if (outcome == MatchStatus.Declined)
                        m.RespondedAt = now;

                    // התרומה חוזרת למאגר הפתוחות
                    if (m.Donation != null && m.Donation.Status == DonationStatus.Matched)
                        m.Donation.Status = DonationStatus.Pending;
                }

                await _matchRepository.UpdateAsync(group[0]);
                await CloseRouteIfDoneAsync(group[0], all, releaseVolunteer: false);
                await RevokeLinkTokensAsync(group[0]);
            });

            if (outcome == MatchStatus.TimedOut && group[0].Volunteer != null)
            {
                await _notifications.NotifyUserAsync(group[0].Volunteer.UserId,
                    "פג זמן התגובה לבקשת ההתנדבות", "הבקשה הועברה למתנדב אחר.", type: "warning");
            }

            await PublishStatusAsync(group);

            // מסמך התיאור, שלב 8: הבקשה עוברת אוטומטית למתנדב הבא - בלי לחכות לריצה השעתית
            foreach (var donationId in group.Select(m => m.DonationId).Distinct())
                ScheduleReassign(donationId);
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

            var now = DateTime.UtcNow;
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
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

            var now = DateTime.UtcNow;
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
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

            await NotifyParticipantsAsync(new[] { match }, $"תרומה #{match.DonationId} נאספה", "המתנדב בדרך לעמותה.");
            await PublishStatusAsync(new[] { match });
        }

        // המתנדב (או מנהל) מסמן שהתרומה נמסרה לעמותה
        public async Task DeliverAsync(Match match)
        {
            if (match.Status != MatchStatus.Confirmed && match.Status != MatchStatus.InPickup && match.Status != MatchStatus.InTransit)
                throw new ArgumentException("לא ניתן לסמן מסירה לשיבוץ במצב הנוכחי.");

            var now = DateTime.UtcNow;
            var group = await GetGroupAsync(match);

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
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

            if (match.Ngo != null)
            {
                await _notifications.NotifyUserAsync(match.Ngo.UserId,
                    $"תרומה #{match.DonationId} נמסרה",
                    "נא לאשר את קבלת התרומה.",
                    link: "/app/ngo/confirm", type: "request");
            }

            if (match.Donation?.Supplier != null)
                await _notifications.NotifyUserAsync(match.Donation.Supplier.UserId, $"תרומה #{match.DonationId} נמסרה לעמותה", null, type: "success");

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

            var now = DateTime.UtcNow;
            var group = await GetGroupAsync(match);

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
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

        public async Task<int> MarkNoShowAsync(Match match, string? reason, bool automatic)
        {
            var all = await GetGroupAsync(match);
            var group = all
                .Where(m => m.IsActive && m.PickedUpAt == null &&
                            (m.Status == MatchStatus.Confirmed || m.Status == MatchStatus.InPickup))
                .ToList();

            if (group.Count == 0)
                throw new ArgumentException("אפשר לסמן No-Show רק לשיבוץ שאושר ועדיין לא נאסף.");

            int volunteerId = group[0].VolunteerId;

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
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
                (automatic ? "זוהה אוטומטית. " : "") + "התרומות הועברו לשיבוץ מחדש מיידי.",
                link: "/app/admin/noshow", type: "warning");

            await PublishStatusAsync(group);

            // מסמך התיאור, שלב 9: שיבוץ מחדש מיידי, בלי לחכות לריצה השעתית
            foreach (var donationId in group.Select(m => m.DonationId).Distinct())
                ScheduleReassign(donationId);

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
                    route.CompletedAt = DateTime.UtcNow;
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

        private async Task NotifyParticipantsAsync(IEnumerable<Match> matches, string subject, string? content)
        {
            var list = matches.ToList();
            var supplierUsers = list.Where(m => m.Donation?.Supplier != null).Select(m => m.Donation.Supplier.UserId).Distinct();
            var ngoUsers = list.Where(m => m.Ngo != null).Select(m => m.Ngo.UserId).Distinct();

            foreach (var userId in supplierUsers.Concat(ngoUsers).Distinct())
                await _notifications.NotifyUserAsync(userId, subject, content, type: "info");
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

        private static string EtaText(IEnumerable<Match> matches)
        {
            var first = matches.Where(m => m.EstimatedArrival.HasValue).Select(m => m.EstimatedArrival!.Value).DefaultIfEmpty(DateTime.UtcNow).Min();
            return LocalClock.ToLocal(first).ToString("HH:mm");
        }
    }
}
