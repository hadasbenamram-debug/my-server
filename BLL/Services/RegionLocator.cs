using DAL.Classes;
using System.Collections.Generic;

namespace BLL.Algorithms
{
    /// <summary>
    /// תיקון 33: האזור של נקודה על המפה (למשל הכתובת של העסק) - לפי המיקום, בלי שהמשתמש יבחר אותו.
    ///
    /// בעבר הספק בחר "אזור האיסוף" בכל תרומה, למרות שמקום האיסוף תמיד היה הכתובת של העסק מההרשמה.
    /// בחירה שגויה (למשל "השרון" לעסק בתל אביב) גרמה לשיבוץ לפי אזור לא נכון - מתנדבים מהאזור האמיתי לא קיבלו אותה.
    ///
    /// הכלל: אזור שהנקודה נמצאת בתוך הרדיוס שלו. אם יש כמה (אזורים חופפים) - זה שהנקודה הכי קרובה למרכזו
    /// ביחס לרדיוס שלו. אם הנקודה מחוץ לכל האזורים - האזור שהמרכז שלו הכי קרוב. O(מספר האזורים).
    /// </summary>
    public static class RegionLocator
    {
        public static Region? Locate(IEnumerable<Region> regions, double latitude, double longitude)
        {
            Region? best = null;
            bool bestInside = false;
            double bestKey = double.MaxValue;

            foreach (var region in regions)
            {
                double km = GeoUtils.HaversineKm(latitude, longitude, region.CenterLatitude, region.CenterLongitude);
                bool inside = region.RadiusKm > 0 && km <= region.RadiusKm;
                double key = inside ? km / region.RadiusKm : km;

                // אזור שהנקודה בתוכו תמיד עדיף על אזור שהיא מחוץ לו; בשוויון - המזהה הנמוך (תוצאה יציבה)
                bool better = best == null
                              || (inside && !bestInside)
                              || (inside == bestInside && (key < bestKey || (key == bestKey && region.Id < best.Id)));
                if (!better) continue;

                best = region;
                bestInside = inside;
                bestKey = key;
            }

            return best;
        }
    }
}
