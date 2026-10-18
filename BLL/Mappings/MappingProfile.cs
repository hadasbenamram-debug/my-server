using AutoMapper;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using System.Linq;

namespace BLL.Mappings
{
    // כל ההמרות בין Entity ל-DTO נמצאות כאן, בשכבת ה-BLL.
    // הפרופיל נבדק בטסט (AssertConfigurationIsValid): כל שדה ביעד ממופה או מסומן Ignore במפורש.
    // מיפויים מ-DTO אל Entity בודקים רק את שדות המקור (MemberList.Source): כל שדה שהלקוח שולח חייב להגיע ליעד,
    // ושדות שהשרת קובע (Id, סטטוס, תאריכים) לא נדרסים מהלקוח.
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // ===================== תרומות =====================
            CreateMap<Donation, DonationResponseDto>()
                .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.ToString()))
                .ForMember(d => d.SupplierName, o => o.MapFrom(s => s.Supplier != null ? s.Supplier.BusinessName : null))
                .ForMember(d => d.RegionName, o => o.MapFrom(s => s.Region != null ? s.Region.RegionName : null))
                .ForMember(d => d.Categories, o => o.MapFrom(s => s.DonationFoodCategories))
                .ForMember(d => d.TotalWeightKg, o => o.MapFrom(s =>
                    s.DonationFoodCategories != null ? s.DonationFoodCategories.Sum(c => c.WeightKg) : 0))
                // פרטי השיבוץ הנוכחי מתמלאים בשירות (DonationService.WithMatch) לפי ההתאמה הפעילה
                .ForMember(d => d.MatchId, o => o.Ignore())
                .ForMember(d => d.MatchStatus, o => o.Ignore())
                .ForMember(d => d.NgoId, o => o.Ignore())
                .ForMember(d => d.NgoName, o => o.Ignore())
                .ForMember(d => d.VolunteerId, o => o.Ignore())
                .ForMember(d => d.VolunteerName, o => o.Ignore())
                .ForMember(d => d.EstimatedArrival, o => o.Ignore());

            // תוקן: בעבר היה כאן ReverseMap - כלומר אפשר היה (בטעות) למפות DTO של תשובה חזרה ל-Entity.
            CreateMap<CreateDonationDto, Donation>(MemberList.Source)
                .ForMember(d => d.DonationFoodCategories, o => o.MapFrom(s => s.Categories))
                // שעת התפוגה מחושבת ומאומתת בשירות (ValidateTimes) ולא מועתקת כמו שהיא
                .ForMember(d => d.ExpirationTime, o => o.Ignore())
                .ForSourceMember(s => s.ExpirationTime, o => o.DoNotValidate())
                // התמונה: השירות בודק שהקובץ הועלה ע"י המשתמש ורק אז ממלא את השדה בעצמו
                .ForMember(d => d.Image, o => o.Ignore())
                .ForSourceMember(s => s.Image, o => o.DoNotValidate())
                .ForMember(d => d.CreatedAt, o => o.Ignore())
                .ForMember(d => d.Status, o => o.Ignore())
                .ForMember(d => d.PriorityScore, o => o.Ignore())
                .ForMember(d => d.UrgencyScore, o => o.Ignore());

            CreateMap<DonationFoodCategory, DonationCategoryLineDto>()
                .ForMember(d => d.CategoryName, o => o.MapFrom(s => s.Category != null ? s.Category.CategoryName : null));
            CreateMap<DonationCategoryLineDto, DonationFoodCategory>(MemberList.Source)
                .ForSourceMember(s => s.CategoryName, o => o.DoNotValidate());

            // תרומות - רשימות מקוצרות (ספק / מנהל)
            CreateMap<Donation, SupplierDonationSummaryDto>()
                .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.ToString()))
                .ForMember(d => d.RegionName, o => o.MapFrom(s => s.Region != null ? s.Region.RegionName : "אזור כללי"));
            CreateMap<Donation, AdminDonationSummaryDto>()
                .ForMember(d => d.Status, o => o.MapFrom(s => s.Status.ToString()))
                .ForMember(d => d.SupplierName, o => o.MapFrom(s => s.Supplier != null ? s.Supplier.BusinessName : "לא ידוע"));

            // ===================== משתמשים =====================
            // תוקן: UserDto.Username היה תמיד null כי בישות השדה נקרא Name. עכשיו ה-DTO משתמש ב-Name.
            CreateMap<User, UserDto>()
                .ForMember(d => d.Role, o => o.MapFrom(s => s.Role.ToString()));
            CreateMap<User, UserSummaryDto>();

            // ===================== משבצות זמינות =====================
            CreateMap<AvailabilitySlot, AvailabilitySlotResponseDto>()
                .ForMember(d => d.Day, o => o.MapFrom(s => (int)s.Day))
                .ForMember(d => d.StartTime, o => o.MapFrom(s => s.StartTime.ToString(@"hh\:mm")))
                .ForMember(d => d.EndTime, o => o.MapFrom(s => s.EndTime.ToString(@"hh\:mm")))
                .ForMember(d => d.RegionName, o => o.MapFrom(s => s.Region != null ? s.Region.RegionName : null))
                .ForMember(d => d.ToRegionName, o => o.MapFrom(s => s.ToRegion != null ? s.ToRegion.RegionName : null));

            // ===================== מתנדבים =====================
            // בלי ReverseMap: VolunteerResponseDto מיועד לתשובות בלבד ולא נכתב חזרה ל-Entity.
            CreateMap<Volunteer, VolunteerResponseDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.User != null ? s.User.Name : null))
                .ForMember(d => d.Email, o => o.MapFrom(s => s.User != null ? s.User.Email : null))
                .ForMember(d => d.Phone, o => o.MapFrom(s => s.User != null ? s.User.Phone : null));
            CreateMap<CreateVolunteerDto, Volunteer>(MemberList.Source);
            // עדכון: ממפים רק את השדות שב-UpdateVolunteerDto. כל השאר נשאר כפי שהוא ב-Entity.
            // תוקף רישיון שלא נשלח (null) לא מוחק את התוקף הקיים.
            CreateMap<UpdateVolunteerDto, Volunteer>(MemberList.Source)
                .ForMember(d => d.DriverLicenseExpiry, o => o.Condition(s => s.DriverLicenseExpiry != null));
            CreateMap<Volunteer, AdminVolunteerListItemDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.User != null ? s.User.Name : "מתנדב"))
                .ForMember(d => d.Phone, o => o.MapFrom(s => s.User != null ? s.User.Phone : null))
                .ForMember(d => d.Email, o => o.MapFrom(s => s.User != null ? s.User.Email : null))
                .ForMember(d => d.Status, o => o.MapFrom(s => s.ApprovalStatus.ToString()));

            // ===================== ספקים =====================
            CreateMap<Supplier, SupplierResponseDto>();
            CreateMap<CreateSupplierDto, Supplier>(MemberList.Source)
                // הקובץ נשמר בשירות (LicenseFileService) ובמסד נשמר רק שם הקובץ
                .ForSourceMember(s => s.LicenseFile, o => o.DoNotValidate());
            CreateMap<Supplier, AdminSupplierListItemDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.BusinessName))
                .ForMember(d => d.Phone, o => o.MapFrom(s => s.User != null ? s.User.Phone : "לא צויין"))
                .ForMember(d => d.Status, o => o.MapFrom(s => s.ApprovalStatus.ToString()));

            // ===================== עמותות =====================
            CreateMap<Ngo, NgoResponseDto>();
            CreateMap<Ngo, NgoPublicDto>();
            CreateMap<Ngo, AdminNgoListItemDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.NgoName))
                .ForMember(d => d.Phone, o => o.MapFrom(s => s.User != null ? s.User.Phone : "לא צויין"))
                .ForMember(d => d.City, o => o.MapFrom(s => s.Address))
                .ForMember(d => d.Status, o => o.MapFrom(s => s.ApprovalStatus.ToString()));

            // ===================== בקשות אישור =====================
            CreateMap<ApprovalRequest, ApprovalRequestResponseDto>();
            CreateMap<ApprovalRequest, PendingApprovalRequestDto>()
                // פרטי הפרופיל מתווספים בשירות (ApprovalRequestService) לפי סוג הבקשה
                .ForMember(d => d.ProfileId, o => o.Ignore())
                .ForMember(d => d.ProfileName, o => o.Ignore())
                .ForMember(d => d.Address, o => o.Ignore())
                .ForMember(d => d.VehicleType, o => o.Ignore())
                .ForMember(d => d.MaxCapacityKg, o => o.Ignore())
                .ForMember(d => d.DriverLicenseExpiry, o => o.Ignore())
                .ForMember(d => d.TaxId, o => o.Ignore())
                .ForMember(d => d.RegistrationNumber, o => o.Ignore())
                .ForMember(d => d.HasLicenseFile, o => o.Ignore());

            // ===================== שידוכים =====================
            CreateMap<Match, MatchResponseDto>()
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => s.AssignedAt))
                .ForMember(d => d.StatusName, o => o.MapFrom(s => s.Status.ToString()))
                .ForMember(d => d.VolunteerName, o => o.MapFrom(s =>
                    s.Volunteer != null && s.Volunteer.User != null ? s.Volunteer.User.Name : null))
                .ForMember(d => d.SupplierName, o => o.MapFrom(s =>
                    s.Donation != null && s.Donation.Supplier != null ? s.Donation.Supplier.BusinessName : null))
                .ForMember(d => d.PickupAddress, o => o.MapFrom(s =>
                    s.Donation != null && s.Donation.Supplier != null ? s.Donation.Supplier.Address : null))
                .ForMember(d => d.PickupLatitude, o => o.MapFrom(s => s.Donation != null ? s.Donation.Latitude : 0))
                .ForMember(d => d.PickupLongitude, o => o.MapFrom(s => s.Donation != null ? s.Donation.Longitude : 0))
                .ForMember(d => d.NgoName, o => o.MapFrom(s => s.Ngo != null ? s.Ngo.NgoName : null))
                .ForMember(d => d.NgoAddress, o => o.MapFrom(s => s.Ngo != null ? s.Ngo.Address : null))
                .ForMember(d => d.NgoLatitude, o => o.MapFrom(s => s.Ngo != null ? s.Ngo.Latitude : 0))
                .ForMember(d => d.NgoLongitude, o => o.MapFrom(s => s.Ngo != null ? s.Ngo.Longitude : 0));

            // ===================== לוגי משלוח =====================
            CreateMap<DeliveryLog, DeliveryLogResponseDto>()
                .ForMember(d => d.Status, o => o.MapFrom(s => s.StatusDescription))
                .ForMember(d => d.Notes, o => o.Ignore());
            CreateMap<CreateDeliveryLogDto, DeliveryLog>(MemberList.Source)
                .ForMember(d => d.StatusDescription, o => o.MapFrom(s =>
                    string.IsNullOrWhiteSpace(s.Notes) ? s.Status : s.Status + ": " + s.Notes))
                .ForSourceMember(s => s.Status, o => o.DoNotValidate())
                .ForSourceMember(s => s.Notes, o => o.DoNotValidate());

            // ===================== משובים =====================
            CreateMap<Feedback, FeedbackResponseDto>()
                .ForMember(d => d.VolunteerId, o => o.MapFrom(s => s.Match != null ? s.Match.VolunteerId : (int?)null))
                .ForMember(d => d.VolunteerName, o => o.MapFrom(s =>
                    s.Match != null && s.Match.Volunteer != null && s.Match.Volunteer.User != null
                        ? s.Match.Volunteer.User.Name : null));

            // ===================== קטגוריות מזון ואזורים =====================
            // תוקן: הרשימות מחזירות DTO עם Id (בעבר CreateFoodCategoryDto / CreateRegionDto - בלי מזהה)
            CreateMap<FoodCategory, FoodCategoryResponseDto>();
            CreateMap<CreateFoodCategoryDto, FoodCategory>(MemberList.Source);
            CreateMap<FoodCategory, CategoryOptionDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.CategoryName));
            CreateMap<Region, RegionResponseDto>();
            CreateMap<CreateRegionDto, Region>(MemberList.Source);

            // ===================== נקודות על המפה (מנהלים בלבד) =====================
            CreateMap<Supplier, MapEntityDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.BusinessName))
                .ForMember(d => d.Type, o => o.MapFrom(_ => "supplier"));
            CreateMap<Ngo, MapEntityDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.NgoName))
                .ForMember(d => d.Type, o => o.MapFrom(_ => "ngo"));
            CreateMap<Volunteer, MapEntityDto>()
                .ForMember(d => d.Name, o => o.MapFrom(s => s.User != null ? s.User.Name : "מתנדב"))
                // המיקום האחרון שהמתנדב שיתף (בעבר תמיד 0,0)
                .ForMember(d => d.Latitude, o => o.MapFrom(s => s.LocationUpdatedAt != null ? s.CurrentLatitude : 0.0))
                .ForMember(d => d.Longitude, o => o.MapFrom(s => s.LocationUpdatedAt != null ? s.CurrentLongitude : 0.0))
                .ForMember(d => d.Address, o => o.MapFrom(_ => ""))
                .ForMember(d => d.Type, o => o.MapFrom(_ => "volunteer"));
        }
    }
}
