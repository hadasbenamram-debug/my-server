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

        public NgoService(
            INgoRepository ngoRepository,
            IUserRepository userRepository,
            IRegionRepository regionRepository,
            IApprovalRequestService approvals,
            CallerProfileService caller,
            IUnitOfWork unitOfWork,
            INotificationService notifications,
            IMapper mapper,
            ILogger<NgoService> logger)
        {
            _ngoRepository = ngoRepository;
            _userRepository = userRepository;
            _regionRepository = regionRepository;
            _approvals = approvals;
            _caller = caller;
            _unitOfWork = unitOfWork;
            _notifications = notifications;
            _mapper = mapper;
            _logger = logger;
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

        public async Task<NgoResponseDto> RegisterWithUserAsync(RegisterNgoDto dto)
        {
            string email = EmailNormalizer.Normalize(dto.Email);

            if (!await _regionRepository.ExistsAsync(dto.RegionId))
                throw new ArgumentException("האזור שנבחר אינו קיים.");

            // הרשמה אנונימית לא מצרפת פרופיל לחשבון קיים
            if (await _userRepository.GetByEmailAsync(email) != null)
                throw new ConflictException("כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו.");

            var open = dto.SunToThuOpenTime ?? DefaultOpen;
            var close = dto.SunToThuCloseTime ?? DefaultClose;
            ValidateHours(open, close, dto.ReceivesOnFriday, dto.FridayOpenTime, dto.FridayCloseTime);

            string tempPassword = TempPasswordGenerator.Generate();
            Ngo? ngoEntity = null;

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var user = new User
                {
                    Name = dto.Name.Trim(),
                    Email = email,
                    Phone = dto.Phone.Trim(),
                    PasswordHash = PasswordHasher.Hash(tempPassword),
                    // תוקן: עמותה קיבלה Role=User בזמן שספק ומתנדב מקבלים את התפקיד שלהם
                    Role = UserRole.Ngo,
                    MustChangePassword = true,
                    SecurityStamp = UserService.NewStamp()
                };

                if (!await _userRepository.TryAddAsync(user))
                    throw new ConflictException("כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו.");

                ngoEntity = new Ngo
                {
                    UserId = user.Id,
                    NgoName = dto.NgoName.Trim(),
                    DailyCapacity = dto.DailyCapacity,
                    Address = dto.Address.Trim(),
                    Latitude = dto.Latitude != 0 ? dto.Latitude : 32.0853,
                    Longitude = dto.Longitude != 0 ? dto.Longitude : 34.7818,
                    RegistrationNumber = dto.RegistrationNumber.Trim(),
                    RegionId = dto.RegionId,
                    SunToThuOpenTime = open,
                    SunToThuCloseTime = close,
                    ReceivesOnFriday = dto.ReceivesOnFriday,
                    FridayOpenTime = dto.ReceivesOnFriday ? dto.FridayOpenTime : null,
                    FridayCloseTime = dto.ReceivesOnFriday ? dto.FridayCloseTime : null,
                    GooglePlaceId = "N/A",
                    ApprovalStatus = ApprovalStatus.Pending
                };

                await _ngoRepository.AddAsync(ngoEntity);
                await _approvals.AddPendingAsync(user.Id, RequestedRole.Ngo, true, "הוגשה בקשת הצטרפות של עמותה.");

                await _notifications.QueueEmailAsync(
                    user.Id,
                    user.Email,
                    "פרטי ההתחברות שלך למערכת הצלת מזון",
                    $"שלום {WebUtility.HtmlEncode(user.Name)},<br/>בקשת ההצטרפות של העמותה שלך התקבלה בהצלחה.<br/>" +
                    $"הסיסמה הזמנית שלך היא: <b>{tempPassword}</b><br/>בכניסה הראשונה תתבקש/י לבחור סיסמה חדשה.",
                    summary: "נשלחו פרטי התחברות");
            });

            return _mapper.Map<NgoResponseDto>(ngoEntity);
        }

        public async Task UpdateMyHoursAsync(int userId, UpdateNgoHoursDto dto)
        {
            var ngo = await _ngoRepository.GetByUserIdAsync(userId)
                      ?? throw new NotFoundException("העמותה לא נמצאה למשתמש זה.");

            var open = dto.SunToThuOpenTime ?? ngo.SunToThuOpenTime;
            var close = dto.SunToThuCloseTime ?? ngo.SunToThuCloseTime;
            ValidateHours(open, close, dto.ReceivesOnFriday, dto.FridayOpenTime, dto.FridayCloseTime);

            ngo.Address = dto.Address.Trim();
            ngo.Latitude = dto.Latitude;
            ngo.Longitude = dto.Longitude;
            ngo.SunToThuOpenTime = open;
            ngo.SunToThuCloseTime = close;
            ngo.ReceivesOnFriday = dto.ReceivesOnFriday;
            ngo.FridayOpenTime = dto.ReceivesOnFriday ? dto.FridayOpenTime : null;
            ngo.FridayCloseTime = dto.ReceivesOnFriday ? dto.FridayCloseTime : null;

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
    }
}
