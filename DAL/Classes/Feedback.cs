using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace DAL.Classes
{
    public class Feedback

    {

        [Key]

        public int Id { get; set; }

        public int MatchId { get; set; }

        public Match Match { get; set; }



        [Range(1, 5)] public int Rating { get; set; } // דירוג התרומה עצמה (איכות/כמות)



        // נוסף: דירוג נפרד למתנדב, כפי שתואר בתהליך הדירוג (עמידה בזמנים, אדיבות וכו')

        [Range(1, 5)] public int VolunteerRating { get; set; }



        public string Comments { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    }


}
