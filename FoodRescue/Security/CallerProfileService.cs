using BLL.Interfaces;
using DAL.Classes;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace FoodRescue.Security
{
    // פרופיל המשתמש המחובר כפי שנקבע מה-JWT ומהמסד (לעולם לא מנתוני הלקוח).
    // במערכת רק שתי רמות הרשאה (ADMIN / USER); סוג המשתמש (מתנדב / ספק / עמותה) נקבע לפי הפרופיל המקושר.
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

    public class CallerProfileService
    {
        private readonly IHttpContextAccessor _http;
        private readonly IVolunteerRepository _volunteers;
        private readonly INgoRepository _ngos;
        private readonly ISupplierRepository _suppliers;
        private CallerProfile? _cached;

        public CallerProfileService(
            IHttpContextAccessor http,
            IVolunteerRepository volunteers,
            INgoRepository ngos,
            ISupplierRepository suppliers)
        {
            _http = http;
            _volunteers = volunteers;
            _ngos = ngos;
            _suppliers = suppliers;
        }

        public async Task<CallerProfile> GetAsync()
        {
            if (_cached != null) return _cached;

            var principal = _http.HttpContext?.User;
            if (!int.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int userId))
            {
                return _cached = new CallerProfile();
            }

            return _cached = new CallerProfile
            {
                UserId = userId,
                IsAdmin = principal!.IsInRole("Admin"),
                Volunteer = await _volunteers.GetByUserIdAsync(userId),
                Ngo = await _ngos.GetByUserIdAsync(userId),
                Supplier = await _suppliers.GetByUserIdAsync(userId)
            };
        }
    }
}
