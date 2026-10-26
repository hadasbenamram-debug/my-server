using Microsoft.AspNetCore.Identity;
using AutoMapper;
using BLL.Algorithms;
using BLL.Exceptions;
using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using BLL.Notifications;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using RequestedRole = DAL.Classes.RequestedRole;

namespace BLL.Services
{
    public class SupplierService : ISupplierService
    {
        private readonly ISupplierRepository _supplierRepository;
        private readonly IUserRepository _userRepository;
        private readonly IApprovalRequestService _approvals;
        private readonly ILicenseFileService _licenseFileService;
        private readonly IGeocodingService _geocoding;
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notifications;
        private readonly IMapper _mapper;
        private readonly ILogger<SupplierService> _logger;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IRegistrationNoticeService _registrationNotices;

        public SupplierService(
            ISupplierRepository supplierRepository,
            IUserRepository userRepository,
            IApprovalRequestService approvals,
            ILicenseFileService licenseFileService,
            IGeocodingService geocoding,
            IUnitOfWork unitOfWork,
            INotificationService notifications,
            IMapper mapper,
            ILogger<SupplierService> logger,
            IPasswordHasher<User> passwordHasher,
            IRegistrationNoticeService registrationNotices)
        {
            _passwordHasher = passwordHasher;
            _registrationNotices = registrationNotices;
            _supplierRepository = supplierRepository;
            _userRepository = userRepository;
            _approvals = approvals;
            _licenseFileService = licenseFileService;
            _geocoding = geocoding;
            _unitOfWork = unitOfWork;
            _notifications = notifications;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<IEnumerable<SupplierResponseDto>> GetAllAsync()
        {
            var suppliers = await _supplierRepository.GetAllAsync();
            return _mapper.Map<IEnumerable<SupplierResponseDto>>(suppliers);
        }

        public async Task<List<AdminSupplierListItemDto>> GetAdminListAsync()
        {
            var suppliers = await _supplierRepository.GetAllWithUserAsync();
            return _mapper.Map<List<AdminSupplierListItemDto>>(suppliers);
        }

        public async Task<SupplierResponseDto?> GetByIdAsync(int id, CurrentUserDto currentUser)
        {
            // מנהל רואה כל ספק. אחרים - רק ספק עם המזהה הזה *וגם* של המשתמש המחובר.
            // null (= "לא נמצא") גם כשהספק קיים אבל לא שלו, כדי לא לחשוף אילו מזהים קיימים.
            Supplier? supplier;
            if (currentUser.IsAdmin)
                supplier = await _supplierRepository.GetByIdAsync(id);
            else if (currentUser.UserId.HasValue)
                supplier = await _supplierRepository.GetOwnedAsync(id, currentUser.UserId.Value);
            else
                supplier = null;

            return supplier == null ? null : _mapper.Map<SupplierResponseDto>(supplier);
        }

        public async Task<SupplierResponseDto?> GetCurrentAsync(int userId)
        {
            var supplier = await _supplierRepository.GetByUserIdAsync(userId);
            return supplier == null ? null : _mapper.Map<SupplierResponseDto>(supplier);
        }

        // תיקון 10: משתמש מחובר מוסיף לחשבון שלו גם תפקיד ספק (למשל מתנדב שיש לו עסק) - אותו מייל ואותה סיסמה.
        // הבקשה ממתינה לאישור מנהל, ועד האישור החשבון ממשיך לעבוד בתפקידים שכבר אושרו.
        // בעבר: ConflictException כשכבר היה פרופיל - עכשיו תוצאה (בלי חריגה).
        public async Task<AddRoleResult> AddRoleAsync(int userId, CreateSupplierDto dto)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return AddRoleResult.UserNotFound;

            if (user.Role == UserRole.Admin)
                return AddRoleResult.NotAvailableForAdmin;

            if (await _supplierRepository.GetByUserIdAsync(userId) != null)
                return AddRoleResult.AlreadyHasRole;

            if (dto.LicenseFile == null || dto.LicenseFile.Length == 0)
                throw new ArgumentException("חובה לצרף קובץ רישיון עסק.");

            // המיקום המדויק לפי הכתובת (Google Maps). לפני שמירת הקובץ, כדי שכתובת שגויה לא תשאיר קובץ יתום.
            var location = await _geocoding.GeocodeAsync(dto.Address);

            // שמירת קובץ הרישיון דרך Service העזר (בתיקייה פרטית; במסד נשמר רק שם הקובץ)
            string licenseFileName = await _licenseFileService.SaveLicenseAsync(dto.LicenseFile);

            var supplierEntity = _mapper.Map<Supplier>(dto);
            supplierEntity.UserId = userId;
            supplierEntity.Latitude = location.Latitude;
            supplierEntity.Longitude = location.Longitude;
            supplierEntity.BusinessLicenseFilePath = licenseFileName;
            supplierEntity.ApprovalStatus = ApprovalStatus.Pending;
            supplierEntity.GooglePlaceId = location.PlaceId;

            try
            {
                await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    await _supplierRepository.AddAsync(supplierEntity);
                    await _approvals.AddPendingAsync(userId, FoodRescue.DTOs.RequestedRole.Supplier, true, "הוגש פרופיל ספק עם רישיון עסק.");
                    await _registrationNotices.NotifyRoleRequestReceivedAsync(user, RoleTitle);
                });
            }
            catch
            {
                _licenseFileService.DeleteLicense(licenseFileName);
                throw;
            }

            return AddRoleResult.Added;
        }

        public async Task<RegistrationResult> RegisterWithUserAsync(RegisterSupplierDto dto)
        {
            if (dto.LicenseFile == null || dto.LicenseFile.Length == 0)
                throw new ArgumentException("חובה לצרף קובץ רישיון עסק.");

            string email = EmailNormalizer.Normalize(dto.Email);

            // הבדיקות של הכתובת והקובץ נעשות לפני בדיקת המייל - כך שגם שגיאה בהן לא מגלה אם המייל רשום.
            // המיקום המדויק לפי הכתובת (Google Maps). לפני שמירת הקובץ, כדי שכתובת שגויה לא תשאיר קובץ יתום.
            var location = await _geocoding.GeocodeAsync(dto.Address);

            // שמירת קובץ הרישיון (בדיקת סיומת, גודל ותוכן) לפני יצירת המשתמש
            string licenseFileName = await _licenseFileService.SaveLicenseAsync(dto.LicenseFile);

            // הרשמה אנונימית לא מצרפת פרופיל לחשבון קיים: אחרת כל אחד שיודע כתובת מייל
            // יכול להוסיף לחשבון של מישהו אחר פרופיל "ממתין לאישור" ולחסום לו את ההתחברות.
            // תוקן: בעבר נזרקה כאן ConflictException שחשפה שהמייל רשום. עכשיו - אותה תשובה, ומייל לבעל החשבון.
            var existing = await _userRepository.GetByEmailAsync(email);
            if (existing != null)
            {
                _licenseFileService.DeleteLicense(licenseFileName);
                await _registrationNotices.NotifyExistingAccountAsync(existing, RoleTitle);
                return RegistrationResult.ExistingAccount;
            }

            string tempPassword = TempPasswordGenerator.Generate();
            bool emailTakenMeanwhile = false;

            try
            {
                // תוקן: המשתמש, הפרופיל ובקשת האישור נשמרים יחד (טרנזקציה), כך שכישלון בפרופיל
                // לא משאיר משתמש "יתום" עם מייל תפוס.
                await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    var user = new User
                    {
                        Name = dto.Name.Trim(),
                        Email = email,
                        Phone = dto.Phone.Trim(),
                        Role = UserRole.Supplier,
                        MustChangePassword = true,
                        SecurityStamp = UserService.NewStamp()
                    };
                    user.PasswordHash = _passwordHasher.HashPassword(user, tempPassword);

                    // נרשם באותו רגע בבקשה אחרת עם אותו מייל - לא יוצרים כלום
                    if (!await _userRepository.TryAddAsync(user))
                    {
                        emailTakenMeanwhile = true;
                        return;
                    }

                    var supplierEntity = new Supplier
                    {
                        UserId = user.Id,
                        BusinessName = dto.BusinessName.Trim(),
                        Address = dto.Address.Trim(),
                        // המיקום נקבע בשרת לפי הכתובת, לא מהלקוח
                        Latitude = location.Latitude,
                        Longitude = location.Longitude,
                        TaxId = dto.TaxId.Trim(),
                        // שם הקובץ נקבע בשרת. הלקוח לא שולח נתיב.
                        BusinessLicenseFilePath = licenseFileName,
                        GooglePlaceId = location.PlaceId,
                        ApprovalStatus = ApprovalStatus.Pending
                    };

                    await _supplierRepository.AddAsync(supplierEntity);
                    await _approvals.AddPendingAsync(user.Id, FoodRescue.DTOs.RequestedRole.Supplier, true, "הוגש פרופיל ספק עם רישיון עסק.");

                    await _notifications.QueueEmailAsync(
                        user.Id,
                        user.Email,
                        "פרטי ההתחברות שלך למערכת הצלת מזון",
                        $"שלום {WebUtility.HtmlEncode(user.Name)},<br/>בקשת ההצטרפות שלך כספק למערכת התקבלה בהצלחה ועברה לאישור מנהל המערכת.<br/>" +
                        $"הסיסמה הזמנית שלך היא: <b>{tempPassword}</b><br/>" +
                        "אפשר יהיה להתחבר אחרי שהמנהל יאשר את הבקשה - נשלח אליך מייל כשזה יקרה. בכניסה הראשונה תתבקש/י לבחור סיסמה חדשה.",
                        summary: "נשלחו פרטי התחברות");
                });
            }
            catch
            {
                // הקובץ נשמר לפני הטרנזקציה - אם ההרשמה נכשלה, לא משאירים קובץ יתום
                _licenseFileService.DeleteLicense(licenseFileName);
                throw;
            }

            if (emailTakenMeanwhile)
            {
                _licenseFileService.DeleteLicense(licenseFileName);
                var winner = await _userRepository.GetByEmailAsync(email);
                if (winner != null)
                    await _registrationNotices.NotifyExistingAccountAsync(winner, RoleTitle);
                return RegistrationResult.ExistingAccount;
            }

            return RegistrationResult.Created;
        }

        private const string RoleTitle = RoleTitles.Supplier;

        public async Task<LicenseFileInfo?> GetLicenseFileAsync(int supplierId)
        {
            var supplier = await _supplierRepository.GetByIdAsync(supplierId);
            if (supplier == null)
                return null;

            return _licenseFileService.GetLicenseFile(supplier.BusinessLicenseFilePath);
        }
    }
}
