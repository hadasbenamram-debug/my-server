using System;
using System.Collections.Generic;
using System.Linq;

namespace BLL.Algorithms
{
    /// <summary>
    /// תיקון 30 - שלב 4 בטבלת האלגוריתמים ("Divide &amp; Conquer גיאוגרפי"), שלא היה ממומש בקוד: עץ KD דו-ממדי
    /// על קווי רוחב/אורך, לצמצום מרחב החיפוש לאזור הרלוונטי.
    ///
    ///   Divide  - ממיינים את הנקודות לפי הציר הנוכחי (רוחב / אורך, לסירוגין) ובוחרים את החציון כשורש.
    ///   Conquer - בונים באותה דרך, רקורסיבית, את תת-העץ של הקטנים מהחציון ואת תת-העץ של הגדולים ממנו.
    ///
    /// חיפוש ברדיוס: מלבן חוסם (במעלות) שמכיל בוודאות את כל העיגול, ירידה רק לענפים שחותכים את המלבן,
    /// ובסוף בדיקה מדויקת לפי Haversine. לכן אין "שלילות שווא": נקודה שבתוך הרדיוס תמיד נמצאת.
    ///
    /// בשימוש במנוע: (1) מתנדבים בטווח החיפוש מהתרומה - לפני כל פנייה ל-Google (נסיעה בכביש לעולם לא קצרה
    /// מקו אווירי, אז מי שרחוק בקו אווירי רחוק גם בכביש); (2) תרומות בטווח האשכול של תרומת העוגן (ל-Knapsack).
    ///
    /// סיבוכיות: בנייה O(n log² n) (מיון בכל רמה), חיפוש O(√n + k) כאשר k = מספר התוצאות, במקום O(n) לכל שאילתה.
    /// </summary>
    public sealed class GeoKdTree<T>
    {
        // ק"מ למעלה אחת, לפי רדיוס כדור הארץ 6,371 ק"מ - אותו רדיוס כמו ב-GeoUtils.HaversineKm
        private const double KmPerDegree = 6371.0 * Math.PI / 180.0;

        // מרווח ביטחון למלבן החוסם (ההחלטה הסופית היא תמיד לפי Haversine)
        private const double BoxMargin = 1.01;

        private sealed class Node
        {
            public T Item = default!;
            public double Lat;
            public double Lon;
            public bool SplitByLatitude;
            public Node? Left;
            public Node? Right;
        }

        private readonly Node? _root;

        public int Count { get; }

        public GeoKdTree(IEnumerable<T> items, Func<T, double> latitude, Func<T, double> longitude)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (latitude == null) throw new ArgumentNullException(nameof(latitude));
            if (longitude == null) throw new ArgumentNullException(nameof(longitude));

            var points = items.Select(i => new Point(i, latitude(i), longitude(i))).ToArray();
            Count = points.Length;
            _root = Build(points, 0, points.Length, splitByLatitude: true);
        }

        private readonly record struct Point(T Item, double Lat, double Lon);

        private static readonly Comparison<Point> ByLatitude = (a, b) => a.Lat.CompareTo(b.Lat);
        private static readonly Comparison<Point> ByLongitude = (a, b) => a.Lon.CompareTo(b.Lon);

        private static Node? Build(Point[] points, int from, int to, bool splitByLatitude)
        {
            if (from >= to) return null;

            // Divide: מיון הקטע לפי הציר הנוכחי - החציון הוא השורש
            Array.Sort(points, from, to - from, Comparer<Point>.Create(splitByLatitude ? ByLatitude : ByLongitude));
            int mid = from + (to - from) / 2;

            // Conquer: כל חצי נבנה רקורסיבית על הציר השני
            return new Node
            {
                Item = points[mid].Item,
                Lat = points[mid].Lat,
                Lon = points[mid].Lon,
                SplitByLatitude = splitByLatitude,
                Left = Build(points, from, mid, !splitByLatitude),
                Right = Build(points, mid + 1, to, !splitByLatitude)
            };
        }

        /// <summary>כל הפריטים שהמרחק שלהם מהנקודה (קו אווירי) הוא לכל היותר radiusKm.</summary>
        public List<T> WithinRadius(double latitude, double longitude, double radiusKm)
        {
            var result = new List<T>();
            if (_root == null || double.IsNaN(radiusKm) || radiusKm < 0)
                return result;

            // המלבן החוסם: ברוחב - הפרש המעלות המקסימלי; באורך - לפי קו הרוחב הקרוב ביותר לקוטב בתוך המלבן
            // (שם מעלת אורך היא הקצרה ביותר, ולכן המלבן רחב מספיק לכל העיגול)
            double dLat = BoxMargin * radiusKm / KmPerDegree;
            double poleward = Math.Min(89.9, Math.Max(Math.Abs(latitude - dLat), Math.Abs(latitude + dLat)));
            double dLon = BoxMargin * radiusKm / (KmPerDegree * Math.Cos(poleward * Math.PI / 180.0));

            var query = new Query(latitude, longitude, radiusKm, latitude - dLat, latitude + dLat);

            if (dLon >= 180)
            {
                Search(_root, query, -180, 180, result);
                return result;
            }

            double minLon = longitude - dLon, maxLon = longitude + dLon;
            Search(_root, query, Math.Max(-180, minLon), Math.Min(180, maxLon), result);

            // מלבן שחוצה את קו התאריך (±180) - גם החלק שבצד השני
            if (minLon < -180) Search(_root, query, minLon + 360, 180, result);
            if (maxLon > 180) Search(_root, query, -180, maxLon - 360, result);

            return result;
        }

        private readonly record struct Query(double Lat, double Lon, double RadiusKm, double MinLat, double MaxLat);

        private static void Search(Node? node, Query q, double minLon, double maxLon, List<T> result)
        {
            if (node == null) return;

            if (node.Lat >= q.MinLat && node.Lat <= q.MaxLat &&
                node.Lon >= minLon && node.Lon <= maxLon &&
                GeoUtils.HaversineKm(q.Lat, q.Lon, node.Lat, node.Lon) <= q.RadiusKm)
            {
                result.Add(node.Item);
            }

            // יורדים רק לצד שהמלבן חותך (ערכים שווים לחציון יכולים להיות בשני הצדדים)
            double key = node.SplitByLatitude ? node.Lat : node.Lon;
            double low = node.SplitByLatitude ? q.MinLat : minLon;
            double high = node.SplitByLatitude ? q.MaxLat : maxLon;

            if (low <= key) Search(node.Left, q, minLon, maxLon, result);
            if (high >= key) Search(node.Right, q, minLon, maxLon, result);
        }
    }
}
