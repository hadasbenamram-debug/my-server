using DAL.Classes;
using System;
using System.ComponentModel.DataAnnotations;

namespace FoodRescue.DTOs
{
    // DTO ליצירת שיבוץ (ללא מזהה, סטטוס התחלתי או תאריך שנקבעים בצורה מאובטחת בשרת)
    public class CreateMatchDto
    {
        [Required(ErrorMessage = "יש לציין מזהה תרומה.")]
        public int DonationId { get; set; }

        [Required(ErrorMessage = "יש לציין מזהה מתנדב.")]
        public int VolunteerId { get; set; }

        [Required(ErrorMessage = "יש לציין מזהה עמותה (NgoId).")]
        public int NgoId { get; set; }
    }

    // DTO מלא להצגה בשרת
    public class MatchResponseDto
    {
        public int Id { get; set; }
        public int DonationId { get; set; }
        public int VolunteerId { get; set; }
        public int NgoId { get; set; }
        public MatchStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? VolunteerRouteId { get; set; }
    }
}