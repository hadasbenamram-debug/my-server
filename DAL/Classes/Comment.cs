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



        // תיקון 18: כל תגובה שייכת לתרומה מסוימת - חובה (NOT NULL במסד).
        // בעבר תגובה יכלה להיות על תרומה או על שיבוץ (MatchId), ולתגובה על שיבוץ לא נשמרה התרומה ברשומה.
        // תשובה לתגובה שייכת תמיד לאותה תרומה של התגובה שאליה עונים.

        public int DonationId { get; set; }//מזהה התרומה שעליה התגובה

        public Donation Donation { get; set; }//התרומה שעליה התגובה

    }


}
