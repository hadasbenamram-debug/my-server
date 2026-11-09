using BLL.Exceptions;
using BLL.Services;
using FoodRescue.DTOs;
using System;
using System.Threading.Tasks;

namespace BLL.Agent
{
    // באיזה תפקיד העוזר עובד בשיחה הזו. לפיו נבחרים הכלים והוראות המערכת.
    public enum AgentRole
    {
        General,    // משתמש מחובר בלי תפקיד מאושר - רק כלים כלליים (לוח שנה)
        Admin,
        Volunteer,
        Supplier,
        Ngo
    }

    // "מי שואל" - נקבע פעם אחת בתחילת כל בקשה (AgentService), מתוך ה-JWT שהקונטרולר קרא.
    // הכלים קוראים את המשתמש מכאן, ולכן לאף כלי אין פרמטר userId / supplierId:
    // המודל לא יכול לבקש נתונים של משתמש אחר, גם אם מישהו יכתוב בצ'אט "תראה לי את התרומות של ספק 5".
    // Scoped: מופע אחד לבקשה, משותף ל-AgentService ולכל מחלקות הכלים.
    public sealed class AgentCaller
    {
        private readonly CallerProfileService _profiles;

        public AgentCaller(CallerProfileService profiles)
        {
            _profiles = profiles;
        }

        public CurrentUserDto User { get; private set; } = new();
        public AgentRole Role { get; private set; } = AgentRole.General;

        // מזהי הפרופילים המאושרים של המשתמש (null = אין לו פרופיל מאושר כזה)
        public int? SupplierId { get; private set; }
        public int? NgoId { get; private set; }

        public int UserId => User.UserId ?? throw new UnauthorizedException("המשתמש אינו מחובר.");

        public async Task InitializeAsync(CurrentUserDto user, string? requestedRole)
        {
            User = user ?? throw new ArgumentNullException(nameof(user));

            var profile = await _profiles.GetAsync(user);

            bool volunteer = profile.IsApprovedVolunteer;
            bool supplier = profile.Supplier?.ApprovalStatus == DAL.Classes.ApprovalStatus.Approved;
            bool ngo = profile.Ngo?.ApprovalStatus == DAL.Classes.ApprovalStatus.Approved;

            SupplierId = supplier ? profile.Supplier!.Id : null;
            NgoId = ngo ? profile.Ngo!.Id : null;

            var requested = Enum.TryParse<AgentRole>(requestedRole, ignoreCase: true, out var parsed) ? parsed : AgentRole.General;
            Role = ResolveRole(user.IsAdmin, requested, volunteer, supplier, ngo);
        }

        // התפקיד שהקליינט ביקש - רק אם הוא באמת מאושר למשתמש. אחרת: התפקיד המאושר הראשון שלו.
        // (מנהל הוא תמיד מנהל: לחשבון מנהל לא מוסיפים תפקידים אחרים.)
        public static AgentRole ResolveRole(bool isAdmin, AgentRole requested, bool volunteer, bool supplier, bool ngo)
        {
            if (isAdmin) return AgentRole.Admin;

            if (requested == AgentRole.Volunteer && volunteer) return AgentRole.Volunteer;
            if (requested == AgentRole.Supplier && supplier) return AgentRole.Supplier;
            if (requested == AgentRole.Ngo && ngo) return AgentRole.Ngo;

            if (volunteer) return AgentRole.Volunteer;
            if (supplier) return AgentRole.Supplier;
            if (ngo) return AgentRole.Ngo;
            return AgentRole.General;
        }

        // DonationService.GetByIdAsync מתיר לכל מתנדב מאושר לראות כל תרומה (כדי שיוכל לאסוף).
        // בשיחה מצמצמים לפי התפקיד הפעיל: ספק רואה רק את התרומות שלו, ועמותה רק את מה שמיועד אליה.
        // כך חשבון שהוא גם ספק וגם מתנדב לא יראה, כשהוא באזור הספק, תרומות של ספקים אחרים.
        public bool CanSee(DonationResponseDto donation) => Role switch
        {
            AgentRole.Admin => true,
            AgentRole.Supplier => SupplierId.HasValue && donation.SupplierId == SupplierId.Value,
            AgentRole.Ngo => NgoId.HasValue && donation.NgoId == NgoId.Value,
            _ => false
        };

        public void RequireAdmin()
        {
            if (Role != AgentRole.Admin || !User.IsAdmin)
                throw new ForbiddenException("הכלי הזה זמין רק למנהל המערכת.");
        }
    }
}
