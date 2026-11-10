using BLL.Notifications;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FoodRescue.Tests
{
    // תיקון 37: מיילים מעוצבים בשפה העיצובית של האתר
    public class EmailTemplateTests
    {
        private const string Site = "https://latet.example";

        [Fact]
        public void Wrap_BuildsABrandedRightToLeftDocument_WithTheSubjectAsTitle()
        {
            var html = EmailTemplate.Wrap("בקשת התנדבות חדשה - לתת באהבה", EmailTemplate.Paragraph("שלום"), Site);

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.Contains("dir=\"rtl\"", html);
            Assert.Contains("לתת</span>", html);                       // הלוגו
            Assert.Contains(EmailTemplate.Tagline, html);
            Assert.Contains("<h1", html);
            Assert.Contains(">בקשת התנדבות חדשה</h1>", html);          // בלי " - לתת באהבה"
            Assert.Contains("<p style=\"margin:0 0 14px;\">שלום</p>", html);
            Assert.Contains($"href=\"{Site}\"", html);
            Assert.True(EmailTemplate.IsWrapped(html));
        }

        [Fact]
        public void Wrap_IsIdempotent_AndStylesPlainLinksOnly()
        {
            var body = "<a href='https://x.test/a'>קישור</a> " + EmailTemplate.Button("https://x.test/b", "כפתור");
            var once = EmailTemplate.Wrap("נושא", body);

            Assert.Equal(once, EmailTemplate.Wrap("נושא", once));
            Assert.Contains("<a style=\"color:#159aa0;font-weight:700;\" href='https://x.test/a'>", once);
            // לכפתור כבר יש עיצוב - לא נוסף לו style שני
            Assert.Equal(1, Regex.Matches(once, "href=\"https://x.test/b\"").Count);
            Assert.DoesNotContain("style=\"color:#159aa0;font-weight:700;\" href=\"https://x.test/b\"", once);
        }

        [Fact]
        public void Button_And_Code_EncodeTheirText()
        {
            var button = EmailTemplate.Button("https://x.test/?a=1&b=<2>", "<script>");
            Assert.Contains("href=\"https://x.test/?a=1&amp;b=&lt;2&gt;\"", button);
            Assert.Contains("&lt;script&gt;", button);
            Assert.DoesNotContain("<script>", button);

            Assert.Contains("&lt;b&gt;", EmailTemplate.Code("<b>"));
            Assert.Contains("שלום &lt;i&gt;דני&lt;/i&gt;,", EmailTemplate.Greeting("<i>דני</i>"));
        }

        [Fact]
        public void Details_SkipsEmptyRows()
        {
            var html = EmailTemplate.Details(("שם", "דנה"), ("טלפון", ""), ("נושא", "שאלה"));

            Assert.Contains("שם", html);
            Assert.Contains("נושא", html);
            Assert.DoesNotContain("טלפון", html);
            Assert.Equal(string.Empty, EmailTemplate.Details(("ריק", " ")));
        }

        [Fact]
        public void PlainText_KeepsTheTextAndTheLinkAddresses()
        {
            var body = EmailTemplate.Greeting("דנה") + EmailTemplate.Paragraph("שורה ראשונה<br/>שורה שנייה") +
                       EmailTemplate.Button("https://x.test/go", "כניסה");
            var text = EmailTemplate.PlainText("איפוס סיסמה - לתת באהבה", body, Site);

            Assert.StartsWith("איפוס סיסמה", text);
            Assert.Contains("שלום דנה,", text);
            Assert.Contains("שורה ראשונה\nשורה שנייה", text);
            Assert.Contains("כניסה (https://x.test/go)", text);
            Assert.DoesNotContain("<", text);
            Assert.EndsWith(Site, text);
        }

        // קוד אימות בן 6 ספרות הוא המספר היחיד בן 6 ספרות במייל (גם צבע כמו #101010 נראה כך - ולכן לא בשימוש)
        [Fact]
        public void TheOnlySixDigitNumberInACodeEmailIsTheCode()
        {
            var body = EmailTemplate.Greeting("דנה") + EmailTemplate.Paragraph("קוד האימות:") + EmailTemplate.Code("482913") +
                       EmailTemplate.Note("תקף ל-15 דקות") + EmailTemplate.Signature();

            var sixDigitNumbers = Regex.Matches(body, @"\b\d{6}\b").Select(m => m.Value).ToList();
            Assert.Equal(new[] { "482913" }, sixDigitNumbers);

            var wrapped = EmailTemplate.Wrap("קוד אימות - לתת באהבה", body, Site);
            Assert.Equal(new[] { "482913", "482913" },   // בתוכן, ובטקסט המקדים (preheader) שמוצג ברשימת המיילים
                Regex.Matches(wrapped, @"\b\d{6}\b").Select(m => m.Value).ToList());
        }
    }
}
