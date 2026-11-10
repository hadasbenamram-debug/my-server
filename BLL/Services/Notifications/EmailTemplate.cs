using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BLL.Notifications
{
    /// <summary>
    /// תיקון 37: מיילים מעוצבים בשפה העיצובית של האתר. בעבר כל מייל היה טקסט עם &lt;br/&gt; בלבד.
    ///
    /// Wrap עוטף כל מייל (בזמן השליחה, ב-SmtpEmailSender) במסגרת אחידה: הלוגו "לתת באהבה ♥" והתגית
    /// "הצלת מזון · קהילה · ערבות הדדית", פס בצבע הטורקיז של האתר, כרטיס לבן עם פינות מעוגלות, כותרת לפי נושא המייל,
    /// וכותרת תחתונה. העטיפה נעשית רק בשליחה - בתור נשמר התוכן עצמו (מוצפן), כמו קודם.
    ///
    /// העזרים (Paragraph, Button, Details, Code, Note...) בונים את התוכן עם עיצוב inline - הדרך היחידה שעובדת
    /// בכל תוכנות הדואר (Gmail, Outlook, Apple Mail, נייד): טבלאות, בלי CSS חיצוני ובלי JavaScript.
    /// כל טקסט של משתמש עובר HtmlEncode לפני שהוא נכנס למייל.
    ///
    /// הצבעים - כמו באתר (src/theme/theme.ts): טורקיז #1bbfc4, טורקיז כהה #159aa0, מנטה רכה #dcebe9, פחם #33383a.
    /// (טקסט כהה: #1a1a1a ולא #101010 - כדי שאף צבע במייל לא ייראה כמו קוד אימות בן 6 ספרות.)
    /// </summary>
    public static class EmailTemplate
    {
        public const string BrandName = "לתת באהבה";
        public const string Tagline = "הצלת מזון · קהילה · ערבות הדדית";

        private const string Teal = "#1bbfc4";
        private const string TealDeep = "#159aa0";
        private const string MintSoft = "#dcebe9";
        private const string Charcoal = "#33383a";
        private const string Ink = "#1a1a1a";
        private const string Muted = "#5f5f5f";
        private const string PageBackground = "#e8e8e8";
        private const string Font = "Heebo, Arial, Helvetica, sans-serif";

        // סימן שהמייל כבר עטוף (כדי לא לעטוף פעמיים)
        private const string Marker = "data-latet-email";

        private static readonly Regex PlainAnchor = new(@"<a(?![^>]*\bstyle\s*=)(\s)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new(@"[ \t ]+", RegexOptions.Compiled);

        // =========================================================
        // העטיפה
        // =========================================================

        public static bool IsWrapped(string? html) =>
            html != null && html.Contains(Marker, StringComparison.Ordinal);

        /// <summary>מסמך HTML מלא: לוגו, כותרת (לפי הנושא, בלי " - לתת באהבה"), התוכן, וכותרת תחתונה.</summary>
        public static string Wrap(string subject, string bodyHtml, string? siteUrl = null)
        {
            bodyHtml ??= string.Empty;
            if (IsWrapped(bodyHtml))
                return bodyHtml;

            string title = Encode(TitleFrom(subject));
            string content = StyleLinks(bodyHtml);
            string preheader = Encode(Truncate(ToPlainText(bodyHtml).Replace('\n', ' '), 110));
            string siteLink = string.IsNullOrWhiteSpace(siteUrl)
                ? string.Empty
                : $" <a href=\"{Attr(siteUrl)}\" target=\"_blank\" style=\"color:{TealDeep};font-weight:700;text-decoration:none;\">לאתר {BrandName}</a>";

            return $@"<!DOCTYPE html>
<html lang=""he"" dir=""rtl"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""x-apple-disable-message-reformatting"">
<meta name=""color-scheme"" content=""light"">
<meta name=""supported-color-schemes"" content=""light"">
<title>{title}</title>
<link href=""https://fonts.googleapis.com/css2?family=Heebo:wght@300;400;500;700&amp;display=swap"" rel=""stylesheet"">
<style>
  body {{ margin:0; padding:0; -webkit-text-size-adjust:100%; }}
  a {{ color:{TealDeep}; }}
  @media only screen and (max-width:620px) {{
    .le-container {{ width:100% !important; border-radius:0 !important; }}
    .le-px {{ padding-left:22px !important; padding-right:22px !important; }}
  }}
</style>
</head>
<body {Marker}=""1"" style=""margin:0;padding:0;background:{PageBackground};direction:rtl;"">
<div style=""display:none;max-height:0;max-width:0;overflow:hidden;opacity:0;color:transparent;mso-hide:all;"">{preheader}</div>
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" dir=""rtl"" style=""background:{PageBackground};"">
<tr><td align=""center"" style=""padding:28px 12px;"">
  <table role=""presentation"" class=""le-container"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"" dir=""rtl""
         style=""width:600px;max-width:600px;background:#ffffff;border-radius:20px;overflow:hidden;"">
    <tr><td class=""le-px"" style=""padding:28px 40px 20px;text-align:right;font-family:{Font};"">
      <span style=""font-size:28px;line-height:1;font-weight:500;color:{Ink};"">לתת</span><span style=""font-size:28px;line-height:1;font-weight:300;color:{Teal};""> באהבה</span>
      <span style=""font-size:22px;line-height:1;color:{Teal};"">&#9829;</span>
      <div style=""margin-top:8px;font-size:11px;letter-spacing:2px;color:{Muted};"">{Tagline}</div>
    </td></tr>
    <tr><td style=""height:4px;line-height:4px;font-size:0;background:{Teal};"">&nbsp;</td></tr>
    <tr><td class=""le-px"" style=""padding:32px 40px 8px;text-align:right;direction:rtl;font-family:{Font};font-size:16px;line-height:1.75;color:{Charcoal};"">
      <h1 style=""margin:0 0 18px;font-family:{Font};font-size:23px;line-height:1.35;font-weight:700;color:{Ink};"">{title}</h1>
      {content}
    </td></tr>
    <tr><td class=""le-px"" style=""padding:18px 40px 30px;text-align:right;font-family:{Font};"">
      <div style=""border-top:1px solid {MintSoft};padding-top:16px;font-size:12px;line-height:1.7;color:{Muted};"">
        ההודעה נשלחה אוטומטית ממערכת &quot;{BrandName}&quot; - מצילים עודפי מזון ומביאים אותם, באהבה, למי שזקוק להם.{siteLink}
      </div>
    </td></tr>
  </table>
</td></tr>
</table>
</body>
</html>";
        }

        /// <summary>גרסת טקסט רגיל של המייל (multipart/alternative) - לתוכנות דואר בלי HTML, ומשפרת את ההגעה לתיבה.</summary>
        public static string PlainText(string subject, string bodyHtml, string? siteUrl = null)
        {
            var text = new StringBuilder();
            text.AppendLine(TitleFrom(subject));
            text.AppendLine();
            text.AppendLine(ToPlainText(bodyHtml ?? string.Empty));
            text.AppendLine();
            text.Append("— ").Append(BrandName).Append(": ").Append(Tagline);
            if (!string.IsNullOrWhiteSpace(siteUrl))
                text.AppendLine().Append(siteUrl);
            return text.ToString();
        }

        // =========================================================
        // עזרים לתוכן
        // =========================================================

        public static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

        public static string Paragraph(string html) =>
            $"<p style=\"margin:0 0 14px;\">{html}</p>";

        public static string Greeting(string? name) =>
            Paragraph($"שלום {Encode(string.IsNullOrWhiteSpace(name) ? "וברוכים הבאים" : name)},");

        public static string Signature() =>
            Paragraph($"תודה,<br/>צוות {BrandName} <span style=\"color:{Teal};\">&#9829;</span>");

        /// <summary>כפתור עגול (כמו באתר): רקע טורקיז, טקסט כהה. עובד גם ב-Outlook (טבלה, לא div).</summary>
        public static string Button(string url, string text) =>
            $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:22px 0 18px;\"><tr>" +
            $"<td style=\"border-radius:999px;background:{Teal};\">" +
            $"<a href=\"{Attr(url)}\" target=\"_blank\" style=\"display:inline-block;padding:13px 32px;border-radius:999px;" +
            $"font-family:{Font};font-size:16px;line-height:1.2;font-weight:700;color:{Ink};text-decoration:none;\">{Encode(text)}</a>" +
            "</td></tr></table>";

        /// <summary>טבלת פרטים (תווית: ערך) על רקע מנטה רכה. הערך הוא HTML - טקסט של משתמש חייב לעבור Encode.</summary>
        public static string Details(params (string Label, string ValueHtml)[] rows) =>
            Details((IEnumerable<(string Label, string ValueHtml)>)rows);

        public static string Details(IEnumerable<(string Label, string ValueHtml)> rows)
        {
            var list = rows.Where(r => !string.IsNullOrWhiteSpace(r.ValueHtml)).ToList();
            if (list.Count == 0)
                return string.Empty;

            var html = new StringBuilder();
            html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" dir=\"rtl\" " +
                        $"style=\"margin:8px 0 18px;background:{MintSoft};border-radius:14px;\">");
            for (int i = 0; i < list.Count; i++)
            {
                string pad = i == 0 ? "14px 18px 6px" : i == list.Count - 1 ? "6px 18px 14px" : "6px 18px";
                html.Append("<tr>")
                    .Append($"<td valign=\"top\" style=\"padding:{pad};width:34%;font-family:{Font};font-size:14px;font-weight:700;color:{Ink};text-align:right;\">{Encode(list[i].Label)}</td>")
                    .Append($"<td valign=\"top\" style=\"padding:{pad};font-family:{Font};font-size:15px;color:{Charcoal};text-align:right;\">{list[i].ValueHtml}</td>")
                    .Append("</tr>");
            }
            html.Append("</table>");
            return html.ToString();
        }

        /// <summary>רשימה ממוספרת (למשל תחנות במסלול). הפריטים הם HTML.</summary>
        public static string NumberedList(IEnumerable<string> itemsHtml)
        {
            var items = itemsHtml.ToList();
            if (items.Count == 0)
                return string.Empty;
            return $"<ol style=\"margin:4px 0 0;padding:0 20px 0 0;\">" +
                   string.Concat(items.Select(i => $"<li style=\"margin:0 0 4px;\">{i}</li>")) +
                   "</ol>";
        }

        /// <summary>קוד / סיסמה זמנית בתיבה בולטת (משמאל לימין, כדי שיהיה קל להעתיק).</summary>
        public static string Code(string value)
        {
            bool longValue = (value ?? string.Empty).Length > 12;
            string size = longValue ? "15px" : "26px";
            string spacing = longValue ? "1px" : "5px";
            return $"<div dir=\"ltr\" style=\"margin:12px 0 18px;padding:14px 18px;background:{MintSoft};border-radius:14px;" +
                   $"font-family:'Courier New',Consolas,monospace;font-size:{size};font-weight:700;letter-spacing:{spacing};" +
                   $"color:{Ink};text-align:center;word-break:break-all;\">{Encode(value)}</div>";
        }

        /// <summary>הערה קטנה ואפורה (למשל "הקישור תקף ל-3 ימים").</summary>
        public static string Note(string html) =>
            $"<p style=\"margin:10px 0 14px;font-size:13px;line-height:1.6;color:{Muted};\">{html}</p>";

        /// <summary>תיבה מודגשת עם פס טורקיז בצד (למשל הודעה של משתמש בטופס צור קשר).</summary>
        public static string Quote(string html) =>
            $"<div style=\"margin:8px 0 18px;padding:12px 16px;border-right:4px solid {Teal};background:#f6faf9;border-radius:8px;color:{Charcoal};\">{html}</div>";

        // =========================================================
        // עזר פנימי
        // =========================================================

        // הנושא בלי הסיומת " - לתת באהבה" (המותג כבר בלוגו)
        private static string TitleFrom(string? subject)
        {
            var title = (subject ?? string.Empty).Trim();
            foreach (var suffix in new[] { " - " + BrandName, " – " + BrandName, " | " + BrandName })
            {
                if (title.EndsWith(suffix, StringComparison.Ordinal))
                    return title[..^suffix.Length].Trim();
            }
            return title.Length == 0 ? BrandName : title;
        }

        // קישורים "רגילים" בתוכן (בלי style) - בצבע של האתר
        private static string StyleLinks(string html) =>
            PlainAnchor.Replace(html, $"<a style=\"color:{TealDeep};font-weight:700;\"$1");

        private static string Attr(string value) => WebUtility.HtmlEncode(value);

        private static string ToPlainText(string html)
        {
            var text = Regex.Replace(html, @"<a\s[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>", "$2 ($1)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<br\s*/?>|</p>|</li>|</tr>|</h\d>|</div>|</ol>|</ul>|</table>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</td>", "  ", RegexOptions.IgnoreCase);
            text = Tags.Replace(text, string.Empty);
            text = WebUtility.HtmlDecode(text);
            var lines = text.Split('\n').Select(l => Spaces.Replace(l, " ").Trim());
            return Regex.Replace(string.Join("\n", lines), @"\n{3,}", "\n\n").Trim();
        }

        private static string Truncate(string text, int max) =>
            text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }
}
