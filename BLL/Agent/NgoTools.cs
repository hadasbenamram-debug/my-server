using BLL.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Agent
{
    public sealed record IncomingDonation(
        int DonationId,
        string Status,
        string Supplier,
        string Items,
        double TotalWeightKg,
        string? Volunteer,
        string? EstimatedPickupArrival,
        string? ReceivedAt,
        int? TrackingRouteId);

    public sealed record ReceivingHours(
        string NgoName,
        string SundayToThursday,
        string Friday,
        string SaturdayNight,
        int DailyCapacityKg);

    // כלים של עמותה: מה בדרך אליה, ומתי היא מקבלת תרומות
    public sealed class NgoTools
    {
        private const int MaxDonations = 15;

        // תרומות שעוד בטיפול: שובצו, נאספו, או נמסרו ומחכות לאישור הקבלה של העמותה
        private static readonly string[] InProgressStatuses = { "Matched", "PickedUp", "Delivered" };

        private readonly IDonationService _donations;
        private readonly INgoService _ngos;
        private readonly AgentCaller _caller;

        public NgoTools(IDonationService donations, INgoService ngos, AgentCaller caller)
        {
            _donations = donations;
            _ngos = ngos;
            _caller = caller;
        }

        [Description("מחזיר את התרומות שמיועדות לעמותה המחוברת (עד 15): מצב, הספק, המוצרים והמשקל, המתנדב שמביא, " +
                     "זמן ההגעה המשוער לאיסוף ומספר המסלול למעקב חי. ברירת מחדל: רק מה שעוד בדרך או ממתין לאישור קבלה.")]
        public async Task<List<IncomingDonation>> GetIncomingDonationsAsync(
            [Description("false = רק מה שעוד בדרך או ממתין לאישור קבלה (לרוב השאלות). true = גם תרומות שכבר התקבלו או הסתיימו (היסטוריה).")]
            bool includeFinished)
        {
            var donations = await _donations.GetMyNgoDonationsAsync(_caller.User);

            var relevant = donations.Where(d => includeFinished || InProgressStatuses.Contains(d.Status));
            relevant = includeFinished
                ? relevant.OrderByDescending(d => d.CreatedAt)                      // היסטוריה: מהחדש לישן
                : relevant.OrderBy(d => d.EstimatedArrival ?? DateTime.MaxValue);   // בדרך: מה שמגיע ראשון - ראשון

            return relevant
                .Take(MaxDonations)
                .Select(d => new IncomingDonation(
                    d.Id,
                    AgentFormat.Donation(d.Status),
                    d.SupplierName ?? "",
                    DonationTools.DescribeItems(d.Categories),
                    Math.Round(d.TotalWeightKg, 1),
                    d.VolunteerName,
                    AgentFormat.DateAndTime(d.EstimatedArrival),
                    AgentFormat.DateAndTime(d.ReceivedAt),
                    d.VolunteerRouteId))
                .ToList();
        }

        [Description("מחזיר את שעות קבלת התרומות של העמותה המחוברת (ראשון-חמישי, שישי, מוצאי שבת) ואת הקיבולת היומית.")]
        public async Task<ReceivingHours?> GetMyReceivingHoursAsync()
        {
            var ngo = await _ngos.GetMineAsync(_caller.UserId);
            if (ngo == null)
                return null;

            const string closed = "סגור";

            return new ReceivingHours(
                ngo.NgoName,
                $"{AgentFormat.Time(ngo.SunToThuOpenTime)}-{AgentFormat.Time(ngo.SunToThuCloseTime)}",
                ngo.ReceivesOnFriday && ngo.FridayOpenTime.HasValue && ngo.FridayCloseTime.HasValue
                    ? $"{AgentFormat.Time(ngo.FridayOpenTime.Value)}-{AgentFormat.Time(ngo.FridayCloseTime.Value)}"
                    : closed,
                ngo.ReceivesOnSaturdayNight && ngo.SaturdayNightOpenTime.HasValue && ngo.SaturdayNightCloseTime.HasValue
                    ? $"{AgentFormat.Time(ngo.SaturdayNightOpenTime.Value)}-{AgentFormat.Time(ngo.SaturdayNightCloseTime.Value)} (רק אחרי צאת השבת)"
                    : closed,
                ngo.DailyCapacity);
        }
    }
}
