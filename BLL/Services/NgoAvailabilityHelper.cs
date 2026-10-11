using BLL.Algoritems;
using BLL.Configuration;
using DAL.Classes;
using System;
using System.Collections.Generic;

namespace BLL.Algorithms
{
    public static class NgoAvailabilityHelper
    {
        /// <summary>
        /// תיקון: (1) בשבת העמותה סגורה (במסמך התיאור: שעות קבלה לימים א'-ה' ולשישי בלבד; הקוד המקורי
        /// "פתח" את העמותה בשבת לפי שעות א'-ה'). (2) השעות מפורשות כשעון מקומי ולא ك-UTC.
        /// מחזיר את קטעי הפתיחה בתוך היום המקומי (חלון שחוצה חצות מפוצל לשני קטעים).
        /// </summary>
        private static void GetOpenSegments(Ngo ngo, DayOfWeek day, List<(TimeSpan Start, TimeSpan End)> into)
        {
           if (day == DayOfWeek.Saturday) return;
        
            TimeSpan open, close;
            if (day == DayOfWeek.Friday)
            {
                if (!ngo.ReceivesOnFriday || !ngo.FridayOpenTime.HasValue || !ngo.FridayCloseTime.HasValue)
                    return;
                open = ngo.FridayOpenTime.Value;
                close = ngo.FridayCloseTime.Value;
            }
            else
            {
                open = ngo.SunToThuOpenTime;
                close = ngo.SunToThuCloseTime;
            }

            if (open <= close)
            {
                into.Add((open, close));
            }
            else
            {
                into.Add((TimeSpan.Zero, close));
                into.Add((open, TimeSpan.FromHours(24)));
            }
        }
        /// <summary>
        /// הרגע המוקדם ביותר בטווח [fromUtc, toUtc] שבו העמותה פתוחה (או null אם אין כזה).
        /// </summary>
        public static DateTime? EarliestOpenAtOrAfter(Ngo ngo, DateTime fromUtc, DateTime toUtc)
        {
            if (ngo == null) throw new ArgumentNullException(nameof(ngo));
            if (toUtc < fromUtc) return null;

            var localFrom = LocalClock.ToLocal(fromUtc);
            var localTo = LocalClock.ToLocal(toUtc);
            var segments = new List<(TimeSpan Start, TimeSpan End)>(2);

            for (var day = localFrom.Date; day <= localTo.Date && (day - localFrom.Date).TotalDays <= 7; day = day.AddDays(1))
            {
                segments.Clear();
                GetOpenSegments(ngo, day.DayOfWeek, segments);

                foreach (var (start, end) in segments)
                {
                    var segStart = day + start;
                    var segEnd = day + end;

                    // תיקון: ביום הנוכחי מתחשבים ב-localFrom, אך בימים הבאים מתחילים ישירות משעת הפתיחה של אותו יום
                    var effectiveStart = day == localFrom.Date
                        ? (segStart > localFrom ? segStart : localFrom)
                        : segStart;

                    var effectiveEnd = segEnd < localTo ? segEnd : localTo;

                    if (effectiveStart <= effectiveEnd)
                        return LocalClock.ToUtc(effectiveStart);
                }
            }

            return null;
        }

        public static bool IsNgoOpenAt(Ngo ngo, DateTime arrival)
        {
            return EarliestOpenAtOrAfter(ngo, arrival, arrival).HasValue;
        }

        /// <summary>
        /// המועד האחרון לאיסוף (UTC): שעת האיסוף האחרונה ביום היצירה (שעון מקומי), ולא מעבר לתפוגה.
        /// </summary>
        public static DateTime GetPickupDateTime(Donation donation)
        {
            var localCreated = LocalClock.ToLocal(donation.CreatedAt);
            var pickupUtc = LocalClock.ToUtc(localCreated.Date + donation.LatestPickupTime);
            return pickupUtc > donation.ExpirationTime ? donation.ExpirationTime : pickupUtc;
        }

        /// <summary>
        /// האם קיים תרחיש איסוף שבו התרומה מגיעה לעמותה בזמן שהיא פתוחה ולפני התפוגה.
        /// תיקון: הקוד המקורי בדק רק את התרחיש הגרוע ביותר (איסוף ברגע האחרון האפשרי) ולכן פסל עמותה
        /// שסגורה בשעת האיסוף האחרונה גם אם ניתן לאסוף מוקדם יותר ולהגיע אליה בזמן.
        /// כאן נבדק כל הטווח [מוקדם ביותר, מאוחר ביותר].
        /// </summary>
        public static bool CanNgoReceiveDonation(Ngo ngo, Donation donation, MatchingPolicyOptions? policy = null, DateTime? asOf = null)
        {
            var p = policy ?? new MatchingPolicyOptions();
            var now = asOf ?? DateTime.UtcNow;

            double distanceKm = GeoUtils.HaversineKm(ngo.Latitude, ngo.Longitude, donation.Latitude, donation.Longitude);
            var travel = TimeSpan.FromMinutes(GeoUtils.EstimateDriveMinutes(distanceKm, p.AverageSpeedKmH, p.RoadDetourFactor));

            var earliestPickup = now > donation.CreatedAt ? now : donation.CreatedAt;
            var latestPickup = GetPickupDateTime(donation);
            if (earliestPickup > latestPickup) return false;

            var earliestArrival = earliestPickup + travel;
            var latestArrival = latestPickup + travel;
            if (latestArrival > donation.ExpirationTime) latestArrival = donation.ExpirationTime;
            if (earliestArrival > latestArrival) return false;

            // הדפסות מעקב כדי לראות את הערכים בזמן ריצה
            Console.WriteLine($"[DEBUG] NGO ID: {ngo.Id} | Donation ID: {donation.Id}");
            Console.WriteLine($"[DEBUG] Distance: {distanceKm:F2} km | Travel Time: {travel.TotalMinutes} minutes");
            Console.WriteLine($"[DEBUG] Earliest Arrival: {earliestArrival} | Latest Arrival: {latestArrival}");

            return EarliestOpenAtOrAfter(ngo, earliestArrival, latestArrival).HasValue;
        }
    }
}