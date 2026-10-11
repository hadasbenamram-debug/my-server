using System;

namespace BLL.Algoritems
{
    public static class GeoUtils
    {
        private const double EarthRadiusKm = 6371.0;

        public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            static double ToRad(double deg) => deg * Math.PI / 180.0;
            double dLat = ToRad(lat2 - lat1), dLon = ToRad(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            // שגיאות עיגול יכולות להוציא a מעט מעל 1 ואז Sqrt(1 - a) הופך ל-NaN
            a = Math.Clamp(a, 0.0, 1.0);
            return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        /// <summary>
        /// הערכת זמן נסיעה מקו אווירי (כשאין נתון אמיתי מ-Google). קו אווירי מקצר את הדרך
        /// בפועל בכ-20%-40%, ולכן מוכפל במקדם עקיפה (ברירת מחדל 1.3).
        /// </summary>
        public static double EstimateDriveMinutes(double straightLineKm, double averageSpeedKmH, double roadDetourFactor)
        {
            if (averageSpeedKmH <= 0) averageSpeedKmH = 40.0;
            if (roadDetourFactor < 1.0) roadDetourFactor = 1.0;
            return straightLineKm * roadDetourFactor / averageSpeedKmH * 60.0;
        }
    }
}
