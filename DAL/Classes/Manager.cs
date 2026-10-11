using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace DAL.Classes
{
    [Index(nameof(UserId), IsUnique = true)]
    public class Manager
    {
        [Key]
        public int Id { get; set; } // מפתח ראשי עצמאי (Identity), עקבי עם Supplier/Volunteer/Ngo

        public int UserId { get; set; } // מזהה המשתמש כמפתח זר נפרד

        [ForeignKey("UserId")]
        public User User { get; set; }

        [Required, MaxLength(50)]
        public string Department { get; set; }

        // נוסף: כל הבקשות שהמנהל הזה טיפל בהן (audit)
        public ICollection<ApprovalRequest> HandledRequests { get; set; }
    }
}