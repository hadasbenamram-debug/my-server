using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System.Threading.Tasks;

namespace BLL.Services
{
    // פרופיל המשתמש המחובר כפי שנקבע מה-JWT ומהמסד (לעולם לא מנתוני הלקוח).
    // במערכת רק שתי רמות הרשאה (ADMIN / USER); סוג המשתמש (מתנדב / ספק / עמותה) נקבע לפי הפרופיל המקושר.
    // הועבר משכבת ה-API ל-BLL: בדיקות הרשאה ובעלות הן לוגיקה עסקית ולכן שייכות ל-Services.
    public class CallerProfile
    {
        public int? UserId { get; init; }
        public bool IsAdmin { get; init; }
        public Volunteer? Volunteer { get; init; }
        public Ngo? Ngo { get; init; }
        public Supplier? Supplier { get; init; }

        public bool IsApprovedVolunteer =>
            Volunteer != null && Volunteer.ApprovalStatus == DAL.Classes.ApprovalStatus.Approved;

        public bool OwnsMatchAsVolunteer(Match match) => Volunteer != null && match.VolunteerId == Volunteer.Id;
        public bool IsMatchNgo(Match match) => Ngo != null && match.NgoId == Ngo.Id;
        public bool OwnsDonation(Donation? donation) => Supplier != null && donation != null && donation.SupplierId == Supplier.Id;
    }

    // טוען את הפרופיל של המשתמש המחובר (פעם אחת לכל בקשה - נשמר במטמון של ה-Scope).
    public class CallerProfileService
    {
        private readonly IVolunteerRepository _volunteers;
        private readonly INgoRepository _ngos;
        private readonly ISupplierRepository _suppliers;

        private CallerProfile? _cached;
        private int? _cachedUserId;
        private bool _cachedIsAdmin;

        public CallerProfileService(
            IVolunteerRepository volunteers,
            INgoRepository ngos,
            ISupplierRepository suppliers)
        {
            _volunteers = volunteers;
            _ngos = ngos;
            _suppliers = suppliers;
        }

        public async Task<CallerProfile> GetAsync(CurrentUserDto currentUser)
        {
            if (currentUser?.UserId == null)
                return new CallerProfile();

            if (_cached != null && _cachedUserId == currentUser.UserId && _cachedIsAdmin == currentUser.IsAdmin)
                return _cached;

            int userId = currentUser.UserId.Value;

            _cached = new CallerProfile
            {
                UserId = userId,
                IsAdmin = currentUser.IsAdmin,
                Volunteer = await _volunteers.GetByUserIdAsync(userId),
                Ngo = await _ngos.GetByUserIdAsync(userId),
                Supplier = await _suppliers.GetByUserIdAsync(userId)
            };
            _cachedUserId = userId;
            _cachedIsAdmin = currentUser.IsAdmin;

            return _cached;
        }
    }
}
