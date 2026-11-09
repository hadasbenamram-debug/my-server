using System;

namespace BLL.Algorithms
{
    /// <summary>
    /// תיקון 19 - בעיית ההשמה (Assignment Problem) בשיטה ההונגרית (Kuhn-Munkres, עם פוטנציאלים).
    ///
    /// משמש את ה-Branch &amp; Bound הגלובלי כ"הרפיה": כל מתנדב לוקח לכל היותר מסלול אחד וכל תרומה מקבלת לכל היותר
    /// מסלול משלה - בלי לבדוק התנגשויות בתחנות הנוספות ובקיבולת העמותות. ההשמה האופטימלית של ההרפיה היא חסם עליון
    /// הדוק לפתרון האמיתי, וכשאין בה התנגשויות - היא עצמה הפתרון האופטימלי.
    ///
    /// משקלים ≥ 0; משקל 0 = "אין זוג כזה" (ההתאמה לא חייבת להיות מלאה).
    /// סיבוכיות: O(n² · m) כאשר n = min(שורות, עמודות), m = max(שורות, עמודות).
    /// </summary>
    public static class HungarianAlgorithm
    {
        /// <summary>ההתאמה בעלת המשקל הכולל המקסימלי. Assignment[row] = העמודה של השורה, או -1 אם היא לא מותאמת.</summary>
        public static (double Value, int[] Assignment) MaxWeightMatching(double[,] weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));

            int rows = weights.GetLength(0), cols = weights.GetLength(1);
            var assignment = new int[rows];
            Array.Fill(assignment, -1);
            if (rows == 0 || cols == 0) return (0, assignment);

            // האלגוריתם דורש n ≤ m - אם יש יותר שורות מעמודות, עובדים על המטריצה המשוחלפת
            bool transpose = rows > cols;
            int n = transpose ? cols : rows;
            int m = transpose ? rows : cols;

            // מינימום עלות, כשהעלות = -משקל. אינדקסים מ-1 (אינדקס 0 הוא "שורש" עזר).
            double Cost(int i, int j) => -(transpose ? weights[j - 1, i - 1] : weights[i - 1, j - 1]);

            var u = new double[n + 1];
            var v = new double[m + 1];
            var p = new int[m + 1];       // p[j] = השורה שמשובצת לעמודה j (0 = פנויה)
            var way = new int[m + 1];
            var minv = new double[m + 1];
            var used = new bool[m + 1];

            for (int i = 1; i <= n; i++)
            {
                p[0] = i;
                int j0 = 0;
                Array.Fill(minv, double.PositiveInfinity);
                Array.Clear(used);

                // מסלול מגדיל (augmenting path) לשורה i, תוך עדכון הפוטנציאלים
                do
                {
                    used[j0] = true;
                    int i0 = p[j0], j1 = 0;
                    double delta = double.PositiveInfinity;

                    for (int j = 1; j <= m; j++)
                    {
                        if (used[j]) continue;
                        double current = Cost(i0, j) - u[i0] - v[j];
                        if (current < minv[j]) { minv[j] = current; way[j] = j0; }
                        if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                    }

                    for (int j = 0; j <= m; j++)
                    {
                        if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                        else minv[j] -= delta;
                    }

                    j0 = j1;
                } while (p[j0] != 0);

                // היפוך המסלול
                do
                {
                    int j1 = way[j0];
                    p[j0] = p[j1];
                    j0 = j1;
                } while (j0 != 0);
            }

            double value = 0;
            for (int j = 1; j <= m; j++)
            {
                if (p[j] == 0) continue;
                int row = transpose ? j - 1 : p[j] - 1;
                int col = transpose ? p[j] - 1 : j - 1;
                double w = weights[row, col];
                if (w > 0)
                {
                    assignment[row] = col;
                    value += w;
                }
            }

            return (value, assignment);
        }
    }
}
