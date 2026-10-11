using BLL.Interfaces;
using DAL;
using DAL.Classes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    /// <summary>
    /// עוטף מתנדב יחד עם המרחק שלו לתרומה הספציפית. המרחק מגיע מ-
    /// GoogleMapsIntegrationService (מרחק/זמן נסיעה אמיתי בכביש, ולא קו אווירי),
    /// בדרך כלל דרך המטמון ב-DistanceMemoizationService.
    /// </summary>
    public class VolunteerCandidate
    {
        public Volunteer Volunteer { get; }
        public double DistanceKm { get; }

        public VolunteerCandidate(Volunteer volunteer, double distanceKm)
        {
            Volunteer = volunteer ?? throw new ArgumentNullException(nameof(volunteer));
            DistanceKm = distanceKm;
        }
    }

    public enum AssignmentOutcome
    {
        Assigned,
        EscalatedNoneEligible,
        EscalatedOutOfRange
    }

    public class AssignmentResult
    {
        public AssignmentOutcome Outcome { get; init; }
        public Volunteer? AssignedVolunteer { get; init; }
    }

    /// <summary>
    /// מוצא מתנדב פנוי לתרומה מתוך רשימה ממוינת, עם Backtracking רקורסיבי:
    /// אם המתנדב הנוכחי לא כשיר, עוברים למתנדב הבא, וחוזר חלילה.
    ///
    /// "Branch &amp; Bound" בפועל: הרשימה חייבת להגיע ממוינת לפי מרחק עולה
    /// (מרחק נסיעה אמיתי מ-Google, לא קו אווירי). ברגע שמגיעים למתנדב שמרחקו
    /// חורג מ-_maxSearchRadiusKm, גוזמים את כל שאר הענף בלי לבדוק אותו כלל.
    ///
    /// תיקון: בדיקת הכשירות הבין-אזורית (IsVolunteerEligible) חושבה בעבר ישירות
    /// לפי asOf.DayOfWeek/TimeOfDay (בד"כ UTC) מול Day/StartTime/EndTime של ה-Slot
    /// (שעון קיר מקומי) - אותו באג טיימזון שתוקן ב-NgoAvailabilityHelper וב-
    /// RegionAvailabilityMatchingService. כעת ממיר ל-LocalClock.ToLocal ומשתמש ב-
    /// AvailabilityHelper.SlotServesRoute המשותפת, במקום להשוות RegionId/ToRegionId ידנית.
    /// </summary>
    public class BacktrackingAssignmentService
    {
        private readonly double _maxSearchRadiusKm;
        private readonly Func<Volunteer, Donation, Ngo, System.Threading.Tasks.Task> _sendRequestToVolunteer;
        private readonly Func<Donation, AssignmentOutcome, System.Threading.Tasks.Task> _triggerEscalation;

        public BacktrackingAssignmentService(
            double maxSearchRadiusKm,
            Func<Volunteer, Donation, Ngo, System.Threading.Tasks.Task> sendRequestToVolunteer,
            Func<Donation, AssignmentOutcome, System.Threading.Tasks.Task> triggerEscalation)
        {
            if (maxSearchRadiusKm <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxSearchRadiusKm));

            _maxSearchRadiusKm = maxSearchRadiusKm;
            _sendRequestToVolunteer = sendRequestToVolunteer ?? throw new ArgumentNullException(nameof(sendRequestToVolunteer));
            _triggerEscalation = triggerEscalation ?? throw new ArgumentNullException(nameof(triggerEscalation));
        }

        // כשל באסקלציה (למשל מייל) לא אמור להפיל את ריצת השידוך
        private async System.Threading.Tasks.Task EscalateAsync(Donation donation, AssignmentOutcome outcome)
        {
            try
            {
                await _triggerEscalation(donation, outcome);
            }
            catch
            {
                // הלוג מטופל ב-AdminEscalationService
            }
        }

        /// <summary>
        /// מקבל גם את ה-Ngo היעד, כדי שאפשר יהיה לבדוק אם מדובר בשידוך
        /// תוך-אזורי (מספיקה זמינות רגילה) או בין-אזורי (נדרשת הצהרת AvailabilitySlot
        /// תואמת). sortedCandidates חייבת להיות ממוינת לפי DistanceKm עולה.
        /// </summary>
        public async System.Threading.Tasks.Task<AssignmentResult> TryAssignDonation(
             Donation donation,
             Ngo targetNgo,
             List<VolunteerCandidate> sortedCandidates,
             DateTime asOf,
             int currentIndex = 0)
        {
            if (donation == null) throw new ArgumentNullException(nameof(donation));
            if (targetNgo == null) throw new ArgumentNullException(nameof(targetNgo));

            if (sortedCandidates == null || currentIndex >= sortedCandidates.Count)
            {
                await EscalateAsync(donation, AssignmentOutcome.EscalatedNoneEligible);
                return new AssignmentResult { Outcome = AssignmentOutcome.EscalatedNoneEligible };
            }

            var candidate = sortedCandidates[currentIndex];

            // *** גיזום ענף (Bound) ***
            if (candidate.DistanceKm > _maxSearchRadiusKm)
            {
                await EscalateAsync(donation, AssignmentOutcome.EscalatedOutOfRange);
                return new AssignmentResult { Outcome = AssignmentOutcome.EscalatedOutOfRange };
            }

            if (IsVolunteerEligible(candidate.Volunteer, donation, targetNgo, asOf))
            {
                try
                {
                    await _sendRequestToVolunteer(candidate.Volunteer, donation, targetNgo);
                }
                catch
                {
                    // Notification failure should not block assignment; log is handled by delegate factory.
                }

                return new AssignmentResult
                {
                    Outcome = AssignmentOutcome.Assigned,
                    AssignedVolunteer = candidate.Volunteer
                };
            }

            // Backtracking: המועמד הנוכחי לא כשיר - חוזרים אחורה וממשיכים למועמד הבא
            return await TryAssignDonation(donation, targetNgo, sortedCandidates, asOf, currentIndex + 1);
        }

        private bool IsVolunteerEligible(Volunteer volunteer, Donation donation, Ngo targetNgo, DateTime asOf)
        {
            bool basicEligible = volunteer.IsAvailable && volunteer.MaxCapacityKg >= donation.TotalWeightKg();
            if (!basicEligible) return false;

            if (donation.RegionId == targetNgo.RegionId)
                return true;

            return AvailabilityHelper.CanServeAt(volunteer, asOf, donation.RegionId, targetNgo.RegionId);
        }
    }
}
