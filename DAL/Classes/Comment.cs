using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace DAL.Classes
{
    public class Comment

    {

        [Key]

        public int Id { get; set; }//מזהה

        [Required, MaxLength(100)] public string Author { get; set; }//מי כתב

        [Required] public string Content { get; set; }//תוכן

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;//תאריך יצירה



        // תמיכה בתגובה תחת תגובה (רקורסיה)

        public int? ParentCommentId { get; set; }//מזהה תגובה אב (אם קיים)

        public Comment ParentComment { get; set; }//תגובה אב (אם קיים)

        public ICollection<Comment> Replies { get; set; } = new List<Comment>();//רשימת תגובות תחת תגובה זו



        // נוסף: קישור חובה לישות שעליה מגיבים - בלי זה לא ניתן לדעת על מה התגובה

        // (בדיוק אחד מהשניים אמור להיות מלא - לאכוף בלוגיקה של ה-BLL)

        public int? DonationId { get; set; }//מזהה התרומה שעליה התגובה (אם קיים)

        public Donation Donation { get; set; }//התרומה שעליה התגובה (אם קיים)



        public int? MatchId { get; set; }//מזהה התאמה שעליה התגובה (אם קיים)

        public Match Match { get; set; }//התאמה שעליה התגובה (אם קיים)

    }


}
