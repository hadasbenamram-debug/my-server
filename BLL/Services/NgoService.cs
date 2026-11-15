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
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using RequestedRole = DAL.Classes.RequestedRole;

namespace BLL.Services
{
    public class NgoService : INgoService
    {
        private static readonly TimeSpan DefaultOpen = TimeSpan.FromHours(9);
        private static readonly TimeSpan DefaultClose = TimeSpan.FromHours(17);

        private readonly INgoRepository _ngoRepository;
        private readonly IUserRepository _userRepository;
        private readonly IRegionRepository _regionRepository;
        private readonly IApprovalRequestService _approvals;
        private readonly CallerProfileService _caller;
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notifications;
        private readonly IMapper _mapper;
        private readonly ILogger<NgoService> _logger;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IGeocodingService _geocoding;
        private readonly IRegistrationNoticeService _registrationNotices;

        public NgoService(
            INgoRepository ngoRepository,
            IUserRepository userRepository,
            IRegionRepository regionRepository,
            IApprovalRequestService approvals,
            CallerProfileService caller,
            IUnitOfWork unitOfWork,
            INotificationService notifications,
            IMapper mapper,
            ILogger<NgoService> logger,
            IPasswordHasher<User> passwordHasher,
            IGeocodingService geocoding,
            IRegistrationNoticeService registrationNotices)
        {
            _passwordHasher = passwordHasher;
            _registrationNotices = registrationNotices;
            _ngoRepository = ngoRepository;
            _userRepository = userRepository;
            _regionRepository = regionRepository;
            _approvals = approvals;
            _caller = caller;
            _unitOfWork = unitOfWork;
            _notifications = notifications;
            _mapper = mapper;
            _logger = logger;
            _geocoding = geocoding;
        }

        public async Task<object> GetNgosForCallerAsync(CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);

            if (caller.IsAdmin)
                return _mapper.Map<IEnumerable<NgoResponseDto>>(await _ngoRepository.GetAllAsync());

            // תוקן: הסינון לעמותות מאושרות נעשה במסד ולא אחרי טעינת כל העמותות לזיכרון
            if (caller.IsApprovedVolunteer)
                return _mapper.Map<IEnumerable<NgoPublicDto>>(await _ngoRepository.GetApprovedAsync());

            throw new ForbiddenException("אין הרשאה לצפות ברשימת העמותות.");
        }

        public async Task<object?> GetNgoForCallerAsync(int id, CurrentUserDto currentUser)
        {
            var ngo = await _ngoRepository.GetByIdAsync(id);
            if (ngo == null)
                return null;

            var caller = await _caller.GetAsync(currentUser);

            if (caller.IsAdmin || (caller.Ngo != null && caller.Ngo.Id == ngo.Id))
                return _mapper.Map<NgoResponseDto>(ngo);

            if (caller.IsApprovedVolunteer && ngo.ApprovalStatus == ApprovalStatus.Approved)
                return _mapper.Map<NgoPublicDto>(ngo);

            return null;
        }

        public async Task<NgoResponseDto?> GetMineAsync(int userId)
        {
            var ngo = await _ngoRepository.GetByUserIdAsync(userId);
            return ngo == null ? null : _mapper.Map<NgoResponseDto>(ngo);
        }

        public async Task<List<AdminNgoListItemDto>> GetAdminListAsync()
        {
            var ngos = await _ngoRepository.GetAllWithUserAsync();
            return _mapper.Map<List<AdminNgoListItemDto>>(ngos);
        }

        public async Task<RegistrationResult> RegisterWithUserAsync(RegisterNgoDto dto)
        {
            string email = EmailNormalizer.Normalize(dto.Email);

            await ValidateProfileAsync(dto);

            // המיקום המדויק לפי הכתובת (Google Maps) - לפני הטרנזקציה, כדי לא להחזיק אותה פתוחה בזמן הפנייה ל-Google.
            // נבדק לפני בדיקת המייל, כך שגם כתובת שגויה לא מגלה אם המייל רשום.
            var location = await _geocoding.GeocodeAsync(dto.Address);
            var regionId = await RegionForLocationAsync(location, dto.RegionId);   // תיקון 82: האזור לפי הכתובת

            // הרשמה אנונימית לא מצרפת פרופיל לחשבון קיים.
            // תוקן: בעבר נזרקה כאן ConflictException שחשפה שהמייל רשום. עכשיו - אותה תשובה, ומייל לבעל החשבון.
            var existing = await _userRepository.GetByEmailAsync(email);
            if (existing != null)
            {
                await _registrationNotices.NotifyExistingAccountAsync(existing, RoleTitle);
                return RegistrationResult.ExistingAccount;
            }

            string tempPassword = TempPasswordGenerator.Generate();
            bool emailTakenMeanwhile = false;

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var user = new User
                {
                    Name = dto.Name.Trim(),
                    Email = email,
                    Phone = dto.Phone.Trim(),
                    // תוקן: עמותה קיבלה Role=User בזמן שספק ומתנדב מקבלים את התפקיד שלהם
                    Role = UserRole.Ngo,
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

                await _ngoRepository.AddAsync(NewNgo(user.Id, dto, location, regionId));
                await _approvals.AddPendingAsync(user.Id, FoodRescue.DTOs.RequestedRole.Ngo, true, "הוגשה בקשת הצטרפות של עמותה.");

                await _notifications.QueueEmailAsync(
                    user.Id,
                    user.Email,
                    "פרטי ההתחברות שלך למערכת הצלת מזון",
                    // תיקון 37: מייל מעוצב (EmailTemplate)
                    EmailTemplate.Greeting(user.Name) +
                    EmailTemplate.Paragraph("בקשת ההצטרפות של העמותה שלך התקבלה בהצלחה ועברה לאישור מנהל המערכת. שמחים שאתם איתנו!") +
                    EmailTemplate.Paragraph("הסיסמה הזמנית שלך:") +
                    EmailTemplate.Code(tempPassword) +
                    EmailTemplate.Note("אפשר יהיה להתחבר אחרי שהמנהל יאשר את הבקשה - נשלח אליך מייל כשזה יקרה. בכניסה הראשונה תתבקש/י לבחור סיסמה חדשה.") +
                    EmailTemplate.Signature(),
                    summary: "נשלחו פרטי התחברות");
            });

            if (emailTakenMeanwhile)
            {
                var winner = await _userRepository.GetByEmailAsync(email);
                if (winner != null)
                    await _registrationNotices.NotifyExistingAccountAsync(winner, RoleTitle);
                return RegistrationResult.ExistingAccount;
            }

            return RegistrationResult.Created;
        }

        private const string RoleTitle = RoleTitles.Ngo;

        // תיקון 10: משתמש מחובר מוסיף לחשבון שלו גם תפקיד עמותה - אותו מייל ואותה סיסמה.
        // הבקשה ממתינה לאישור מנהל, ועד האישור החשבון ממשיך לעבוד בתפקידים שכבר אושרו.
        public async Task<AddRoleResult> AddRoleAsync(int userId, CreateNgoDto dto)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return AddRoleResult.UserNotFound;

            if (user.Role == UserRole.Admin)
                return AddRoleResult.NotAvailableForAdmin;

            if (await _ngoRepository.GetByUserIdAsync(userId) != null)
                return AddRoleResult.AlreadyHasRole;

            await ValidateProfileAsync(dto);
            var location = await _geocoding.GeocodeAsync(dto.Address);
            var regionId = await RegionForLocationAsync(location, dto.RegionId);   // תיקון 82: האזור לפי הכתובת

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _ngoRepository.AddAsync(NewNgo(userId, dto, location, regionId));
                await _approvals.AddPendingAsync(userId, FoodRescue.DTOs.RequestedRole.Ngo, true, "הוגשה בקשת הצטרפות של עמותה (תפקיד נוסף לחשבון קיים).");
                await _registrationNotices.NotifyRoleRequestReceivedAsync(user, RoleTitle);
            });

            return AddRoleResult.Added;
        }

        // בדיקות פרופיל העמותה - משותפות להרשמה חדשה ולהוספת תפקיד
        private async Task ValidateProfileAsync(INgoProfileFields dto)
        {
            if (!await _regionRepository.ExistsAsync(dto.RegionId))
                throw new ArgumentException("האזור שנבחר אינו קיים.");

            ValidateHours(dto.SunToThuOpenTime ?? DefaultOpen, dto.SunToThuCloseTime ?? DefaultClose,
                dto.ReceivesOnFriday, dto.FridayOpenTime, dto.FridayCloseTime);
            ValidateSaturdayNightHours(dto.ReceivesOnSaturdayNight, dto.SaturdayNightOpenTime, dto.SaturdayNightCloseTime);
        }

        // תיקון 82: אזור העמותה נקבע לפי המיקום של הכתובת (האזור שהמרכז שלו הכי קרוב), כמו אזור של תרומה.
        // בעבר האזור נבחר ידנית בהרשמה, וברירת המחדל הייתה האזור הראשון ברשימה ("ירושלים והסביבה") -
        // כך עמותה בפתח תקווה נרשמה בירושלים, ומתנדבים מהמרכז לא שובצו אליה.
        // fallbackRegionId - רק אם אין אזורים במערכת (לא אמור לקרות).
        private async Task<int> RegionForLocationAsync(GeocodeResult location, int fallbackRegionId)
        {
            var region = RegionLocator.Locate(await _regionRepository.GetAllAsync(), location.Latitude, location.Longitude);
            return region?.Id ?? fallbackRegionId;
        }

        private static Ngo NewNgo(int userId, INgoProfileFields dto, GeocodeResult location, int regionId) => new()
        {
            UserId = userId,
            NgoName = dto.NgoName.Trim(),
            DailyCapacity = dto.DailyCapacity,
            Address = dto.Address.Trim(),
            // המיקום נקבע בשרת לפי הכתובת, לא מהלקוח
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            RegistrationNumber = dto.RegistrationNumber.Trim(),
            RegionId = regionId,                    // תיקון 82: לפי הכתובת
            SunToThuOpenTime = dto.SunToThuOpenTime ?? DefaultOpen,
            SunToThuCloseTime = dto.SunToThuCloseTime ?? DefaultClose,
            ReceivesOnFriday = dto.ReceivesOnFriday,
            FridayOpenTime = dto.ReceivesOnFriday ? dto.FridayOpenTime : null,
            FridayCloseTime = dto.ReceivesOnFriday ? dto.FridayCloseTime : null,
            ReceivesOnSaturdayNight = dto.ReceivesOnSaturdayNight,
            SaturdayNightOpenTime = dto.ReceivesOnSaturdayNight ? dto.SaturdayNightOpenTime : null,
            SaturdayNightCloseTime = dto.ReceivesOnSaturdayNight ? dto.SaturdayNightCloseTime : null,
            GooglePlaceId = location.PlaceId,
            ApprovalStatus = ApprovalStatus.Pending
        };

        public async Task UpdateMyHoursAsync(int userId, UpdateNgoHoursDto dto)
        {
            var ngo = await _ngoRepository.GetByUserIdAsync(userId)
                      ?? throw new NotFoundException("העמותה לא נמצאה למשתמש זה.");

            var open = dto.SunToThuOpenTime ?? ngo.SunToThuOpenTime;
            var close = dto.SunToThuCloseTime ?? ngo.SunToThuCloseTime;
            ValidateHours(open, close, dto.ReceivesOnFriday, dto.FridayOpenTime, dto.FridayCloseTime);
            ValidateSaturdayNightHours(dto.ReceivesOnSaturdayNight, dto.SaturdayNightOpenTime, dto.SaturdayNightCloseTime);

            // בכל שמירה המיקום מחושב מחדש לפי הכתובת (Google Maps), כך שגם מיקום ישן ושגוי מתתקן
            var location = await _geocoding.GeocodeAsync(dto.Address);

            ngo.Address = dto.Address.Trim();
            ngo.Latitude = location.Latitude;
            ngo.Longitude = location.Longitude;
            // תיקון 82: גם האזור מתעדכן לפי הכתובת (כך עמותה שנרשמה באזור שגוי מתתקנת בשמירה הבאה)
            ngo.RegionId = await RegionForLocationAsync(location, ngo.RegionId);
            ngo.GooglePlaceId = location.PlaceId;
            ngo.SunToThuOpenTime = open;
            ngo.SunToThuCloseTime = close;
            ngo.ReceivesOnFriday = dto.ReceivesOnFriday;
            ngo.FridayOpenTime = dto.ReceivesOnFriday ? dto.FridayOpenTime : null;
            ngo.FridayCloseTime = dto.ReceivesOnFriday ? dto.FridayCloseTime : null;
            ngo.ReceivesOnSaturdayNight = dto.ReceivesOnSaturdayNight;
            ngo.SaturdayNightOpenTime = dto.ReceivesOnSaturdayNight ? dto.SaturdayNightOpenTime : null;
            ngo.SaturdayNightCloseTime = dto.ReceivesOnSaturdayNight ? dto.SaturdayNightCloseTime : null;

            await _ngoRepository.UpdateAsync(ngo);
        }

        private static void ValidateHours(TimeSpan open, TimeSpan close, bool friday, TimeSpan? fridayOpen, TimeSpan? fridayClose)
        {
            if (open < TimeSpan.Zero || close > TimeSpan.FromDays(1) || open >= close)
                throw new ArgumentException("שעת הפתיחה בימים א'-ה' חייבת להיות לפני שעת הסגירה.");

            if (!friday)
                return;

            if (fridayOpen == null || fridayClose == null)
                throw new ArgumentException("יש להזין שעות קבלה ליום שישי.");

            if (fridayOpen.Value >= fridayClose.Value)
                throw new ArgumentException("שעת הפתיחה ביום שישי חייבת להיות לפני שעת הסגירה.");
        }

        // תיקון 9: בשבת עצמה אין קבלת תרומות - רק במוצאי שבת, משעה 17:00 ועד חצות
        // (המנוע משבץ בפועל רק אחרי צאת השבת, לפי ShabbatClock)
        private static void ValidateSaturdayNightHours(bool receives, TimeSpan? open, TimeSpan? close)
        {
            if (!receives)
                return;

            if (open == null || close == null)
                throw new ArgumentException("יש להזין שעות קבלה למוצאי שבת.");

            if (open.Value < ShabbatClock.EarliestSaturdayNightTime)
                throw new ArgumentException($"במוצאי שבת אפשר לקבל תרומות רק משעה {ShabbatClock.EarliestSaturdayNightTime:hh\\:mm} ואילך.");

            if (open.Value >= close.Value || close.Value >= TimeSpan.FromDays(1))
                throw new ArgumentException("שעת הפתיחה במוצאי שבת חייבת להיות לפני שעת הסגירה (עד חצות).");
        }
    }
}
