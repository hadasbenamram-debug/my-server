using AutoMapper;
using BLL.Algorithms;
using BLL.Exceptions;
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
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notifications;
        private readonly IMapper _mapper;
        private readonly ILogger<SupplierService> _logger;

        public SupplierService(
            ISupplierRepository supplierRepository,
            IUserRepository userRepository,
            IApprovalRequestService approvals,
            ILicenseFileService licenseFileService,
            IUnitOfWork unitOfWork,
            INotificationService notifications,
            IMapper mapper,
            ILogger<SupplierService> logger)
        {
            _supplierRepository = supplierRepository;
            _userRepository = userRepository;
            _approvals = approvals;
            _licenseFileService = licenseFileService;
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
            // תוקן: חיפוש לפי מזהה ספק בלבד (בעבר גם לפי מזהה משתמש - דו-משמעי)
            var supplier = await _supplierRepository.GetByIdAsync(id);
            if (supplier == null)
                return null;

            // בדיקת בעלות: null (= "לא נמצא") כדי לא לחשוף שהמזהה קיים.
            if (!currentUser.IsAdmin && supplier.UserId != currentUser.UserId)
                return null;

            return _mapper.Map<SupplierResponseDto>(supplier);
        }

        public async Task<SupplierResponseDto?> GetCurrentAsync(int userId)
        {
            var supplier = await _supplierRepository.GetByUserIdAsync(userId);
            return supplier == null ? null : _mapper.Map<SupplierResponseDto>(supplier);
        }

        public async Task<SupplierResponseDto> CreateAsync(int userId, CreateSupplierDto dto)
        {
            var user = await _userRepository.GetByIdAsync(userId)
                       ?? throw new UnauthorizedException("המשתמש המחובר לא נמצא.");

            if (await _supplierRepository.GetByUserIdAsync(userId) != null)
                throw new ConflictException("למשתמש זה כבר קיים פרופיל ספק.");

            if (dto.LicenseFile == null || dto.LicenseFile.Length == 0)
                throw new ArgumentException("חובה לצרף קובץ רישיון עסק.");

            // שמירת קובץ הרישיון דרך Service העזר (בתיקייה פרטית; במסד נשמר רק שם הקובץ)
            string licenseFileName = await _licenseFileService.SaveLicenseAsync(dto.LicenseFile);

            var supplierEntity = _mapper.Map<Supplier>(dto);
            supplierEntity.UserId = userId;
            supplierEntity.BusinessLicenseFilePath = licenseFileName;
            supplierEntity.ApprovalStatus = ApprovalStatus.Pending;
            supplierEntity.GooglePlaceId = "N/A";

            try
            {
                await _unitOfWork.ExecuteInTransactionAsync(async () =>
                {
                    await _supplierRepository.AddAsync(supplierEntity);
                    await _approvals.AddPendingAsync(userId, RequestedRole.Supplier, true, "הוגש פרופיל ספק עם רישיון עסק.");
                });
            }
            catch
            {
                _licenseFileService.DeleteLicense(licenseFileName);
                throw;
            }

            return _mapper.Map<SupplierResponseDto>(supplierEntity);
        }

        public async Task<SupplierResponseDto> RegisterWithUserAsync(RegisterSupplierDto dto)
        {
            if (dto.LicenseFile == null || dto.LicenseFile.Length == 0)
                throw new ArgumentException("חובה לצרף קובץ רישיון עסק.");

            string email = EmailNormalizer.Normalize(dto.Email);

            // הרשמה אנונימית לא מצרפת פרופיל לחשבון קיים: אחרת כל אחד שיודע כתובת מייל
            // יכול להוסיף לחשבון של מישהו אחר פרופיל "ממתין לאישור" ולחסום לו את ההתחברות.
            if (await _userRepository.GetByEmailAsync(email) != null)
                throw new ConflictException("כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו.");

            // שמירת קובץ הרישיון (בדיקת סיומת, גודל ותוכן) לפני יצירת המשתמש
            string licenseFileName = await _licenseFileService.SaveLicenseAsync(dto.LicenseFile);
            string tempPassword = TempPasswordGenerator.Generate();
            Supplier? supplierEntity = null;

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
                        PasswordHash = PasswordHasher.Hash(tempPassword),
                        Role = UserRole.Supplier,
                        MustChangePassword = true,
                        SecurityStamp = UserService.NewStamp()
                    };

                    if (!await _userRepository.TryAddAsync(user))
                        throw new ConflictException("כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו.");

                    supplierEntity = new Supplier
                    {
                        UserId = user.Id,
                        BusinessName = dto.BusinessName.Trim(),
                        Address = dto.Address.Trim(),
                        Latitude = dto.Latitude != 0 ? dto.Latitude : 32.0853,
                        Longitude = dto.Longitude != 0 ? dto.Longitude : 34.7818,
                        TaxId = dto.TaxId.Trim(),
                        // שם הקובץ נקבע בשרת. הלקוח לא שולח נתיב.
                        BusinessLicenseFilePath = licenseFileName,
                        GooglePlaceId = "N/A",
                        ApprovalStatus = ApprovalStatus.Pending
                    };

                    await _supplierRepository.AddAsync(supplierEntity);
                    await _approvals.AddPendingAsync(user.Id, RequestedRole.Supplier, true, "הוגש פרופיל ספק עם רישיון עסק.");

                    await _notifications.QueueEmailAsync(
                        user.Id,
                        user.Email,
                        "פרטי ההתחברות שלך למערכת הצלת מזון",
                        $"שלום {WebUtility.HtmlEncode(user.Name)},<br/>בקשת ההצטרפות שלך כספק למערכת התקבלה בהצלחה.<br/>" +
                        $"הסיסמה הזמנית שלך היא: <b>{tempPassword}</b><br/>בכניסה הראשונה תתבקש/י לבחור סיסמה חדשה.",
                        summary: "נשלחו פרטי התחברות");
                });
            }
            catch
            {
                // הקובץ נשמר לפני הטרנזקציה - אם ההרשמה נכשלה, לא משאירים קובץ יתום
                _licenseFileService.DeleteLicense(licenseFileName);
                throw;
            }

            return _mapper.Map<SupplierResponseDto>(supplierEntity);
        }

        public async Task<LicenseFileInfo?> GetLicenseFileAsync(int supplierId)
        {
            var supplier = await _supplierRepository.GetByIdAsync(supplierId);
            if (supplier == null)
                return null;

            return _licenseFileService.GetLicenseFile(supplier.BusinessLicenseFilePath);
        }
    }
}
