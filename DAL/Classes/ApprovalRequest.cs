using DAL.Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace DAL
{
    public class ApprovalRequest
    {
        [Key]

        public int Id { get; set; }



        public int UserId { get; set; }

        public User User { get; set; }



        [Required] public RequestedRole RequestedRole { get; set; }

        public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;



        // תוצאות הסינון האוטומטי, לשקיפות מול המנהל

        public bool PassedAutomaticValidation { get; set; }

        public string ValidationDetails { get; set; } // JSON/טקסט חופשי עם תוצאות הבדיקות



        public int? HandledByManagerId { get; set; }

        public Manager HandledByManager { get; set; }



        public string RejectionReason { get; set; }



        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? HandledAt { get; set; }

    }


}

