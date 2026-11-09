using BLL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Agent
{
    // מה שהכלים מחזירים למודל: רק השדות שהוא צריך כדי לענות (כל מה שנשלח למודל עולה כסף),
    // בלי פרטים אישיים (טלפון, מייל, ת"ז) ובלי מיקומים - זמנים כבר בשעון ישראל וסטטוסים בעברית.
    public sealed record DonationSummary(int Id, string Status, string Region, string CreatedAt, string ExpiresAt, string LatestPickupTime);

    public sealed record DonationDetails(
        int Id,
        string Status,
        string Supplier,
        string? Region,
        string Items,
        double TotalWeightKg,
        string ExpiresAt,
        string LatestPickupTime,
        string? Ngo,
        string? Volunteer,
        string? DeliveryStatus,
        string? EstimatedPickupArrival,
        int? TrackingRouteId);

    // כלים על תרומות. כל כלי עובר דרך ה-Service הקיים (ומשם ל-Repository), כמו כל פעולה אחרת בפרויקט -
    // כולל בדיקות ההרשאה שכבר קיימות ב-DonationService.
    public sealed class DonationTools
    {
        private const int MaxDonations = 15;

        private static readonly string[] OpenStatuses = { "Pending", "Matched", "PickedUp", "Delivered" };
        private static readonly string[] ClosedStatuses = { "Expired", "Cancelled" };

        private readonly IDonationService _donations;
        private readonly AgentCaller _caller;

        public DonationTools(IDonationService donations, AgentCaller caller)
        {
            _donations = donations;
            _caller = caller;
        }

        [Description("מחזיר את התרומות של הספק המחובר, מהחדשה לישנה (עד 15): מזהה, מצב, אזור, מתי נוצרה, תוקף ושעת האיסוף האחרונה.")]
        // הפרמטרים של הכלים פשוטים ומחייבים (בלי null ובלי ערך ברירת מחדל): כך הסכמה שנשלחת למודל
        // היא רק "type": "string" - כמו בדוגמה של המדריך, ש-Gemini מקבל בלי בעיה
        public async Task<List<DonationSummary>> GetMyDonationsAsync(
            [Description("סינון: open = פתוחות (ממתינות לשיבוץ או בטיפול), done = הושלמו, closed = פגו או בוטלו, all = כל התרומות. אם לא ברור - all.")]
            string filter)
        {
            var all = await _donations.GetMySupplierDonationsAsync(_caller.UserId);

            IEnumerable<SupplierDonationSummaryDto> selected = filter?.Trim().ToLowerInvariant() switch
            {
                "open" => all.Where(d => OpenStatuses.Contains(d.Status)),
                "done" => all.Where(d => d.Status == "Completed"),
                "closed" => all.Where(d => ClosedStatuses.Contains(d.Status)),
                _ => all
            };

            return selected
                .OrderByDescending(d => d.CreatedAt)
                .Take(MaxDonations)
                .Select(d => new DonationSummary(
                    d.Id,
                    AgentFormat.Donation(d.Status),
                    d.RegionName,
                    AgentFormat.DateAndTime(d.CreatedAt),
                    AgentFormat.DateAndTime(d.ExpirationTime),
                    AgentFormat.Time(d.LatestPickupTime)))
                .ToList();
        }

        [Description("מחזיר את כל הפרטים של תרומה אחת לפי מזהה: המצב, המוצרים והמשקל, העמותה שהיא מיועדת אליה, המתנדב, " +
                     "זמן ההגעה המשוער לאיסוף, ומספר המסלול למעקב חי. מחזיר null אם התרומה לא נמצאה או שאינה שייכת למשתמש.")]
        public async Task<DonationDetails?> GetDonationDetailsAsync(
            [Description("מזהה התרומה (מספר), למשל 42")] int donationId)
        {
            var donation = await _donations.GetByIdAsync(donationId, _caller.User);

            // בדיקה נוספת לפי התפקיד הפעיל (ראו AgentCaller.CanSee) - אותה תשובה כמו "לא נמצאה"
            if (donation == null || !_caller.CanSee(donation))
                return null;

            return new DonationDetails(
                donation.Id,
                AgentFormat.Donation(donation.Status),
                donation.SupplierName ?? "",
                donation.RegionName,
                DescribeItems(donation.Categories),
                Math.Round(donation.TotalWeightKg, 1),
                AgentFormat.DateAndTime(donation.ExpirationTime),
                AgentFormat.Time(donation.LatestPickupTime),
                donation.NgoName,
                donation.VolunteerName,
                donation.MatchStatus == null ? null : AgentFormat.Match(donation.MatchStatus),
                AgentFormat.DateAndTime(donation.EstimatedArrival),
                donation.VolunteerRouteId);
        }

        // "מאפים ×3 (4.5 ק"ג), ירקות ×2 (6 ק"ג)" - טקסט קצר במקום רשימת אובייקטים, כדי לחסוך בטוקנים
        internal static string DescribeItems(IEnumerable<DonationCategoryLineDto>? items) =>
            items == null
                ? ""
                : string.Join(", ", items.Select(i => $"{i.CategoryName ?? "קטגוריה " + i.CategoryId} ×{i.Quantity} ({Math.Round(i.WeightKg, 1)} ק\"ג)"));
    }
}
