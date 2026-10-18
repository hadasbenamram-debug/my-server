using Microsoft.AspNetCore.Identity;
using AutoMapper;
using BLL.Algorithms;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Notifications;
using DAL;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using FoodRescue.DTOs.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using RequestedRole = DAL.Classes.RequestedRole;

namespace BLL.Services
{
    public class VolunteerService : IVolunteerService
    {
        private const string VolunteerNotFound = "המתנדב לא נמצא.";
        private const int MaxSlots = 60;

        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IUserRepository _userRepository;
        private readonly IAvailabilitySlotRepository _availabilitySlotRepository;
        private readonly IRegionRepository _regionRepository;
        private readonly IVolunteerRouteRepository _routeRepository;
        private readonly IApprovalRequestService _approvals;
        private readonly IValidationService _validation;
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notifications;
        private readonly IMapper _mapper;
        private readonly ILogger<VolunteerService> _logger;
        private readonly IPasswordHasher<User> _passwordHasher;

        public VolunteerService(
            IVolunteerRepository volunteerRepository,
            IUserRepository userRepository,
            IAvailabilitySlotRepository availabilitySlotRepository,
            IRegionRepository regionRepository,
            IVolunteerRouteRepository routeRepository,
            IApprovalRequestService approvals,
            IValidationService validation,
            IUnitOfWork unitOfWork,
            INotificationService notifications,
            IMapper mapper,
            ILogger<VolunteerService> logger,
            IPasswordHasher<User> passwordHasher)
        {
            _passwordHasher = passwordHasher;
            _volunteerRepository = volunteerRepository;
            _userRepository = userRepository;
            _availabilitySlotRepository = availabilitySlotRepository;
            _regionRepository = regionRepository;
            _routeRepository = routeRepository;
            _approvals = approvals;
            _validation = validation;
            _unitOfWork = unitOfWork;
            _notifications = notifications;
            _mapper = mapper;
            _logger = logger;
        }

        // =========================================================
        // זמינות של המתנדב המחובר
        // =========================================================

        public async Task<List<AvailabilitySlotResponseDto>> GetMyAvailabilityAsync(int userId)
        {
            var volunteer = await _volunteerRepository.GetByUserIdAsync(userId)
                            ?? throw new NotFoundException(VolunteerNotFound);

            var slots = await _availabilitySlotRepository.GetByVolunteerIdWithRegionsAsync(volunteer.Id);
            return _mapper.Map<List<AvailabilitySlotResponseDto>>(slots);
        }

        public async Task SaveMyAvailabilityAsync(int userId, List<CreateAvailabilitySlotDto> slots)
        {
            var volunteer = await _volunteerRepository.GetByUserIdAsync(userId)
                            ?? throw new NotFoundException(VolunteerNotFound);

            var newSlots = await BuildSlotsAsync(volunteer.Id, slots);
            await _availabilitySlotRepository.ReplaceForVolunteerAsync(volunteer.Id, newSlots);
        }

        // =========================================================
        // ניהול מתנדבים (מנהל)
        // =========================================================

        public async Task<List<AdminVolunteerListItemDto>> GetAllForAdminAsync()
        {
            var volunteers = await _volunteerRepository.GetAllWithUserAsync();
            return _mapper.Map<List<AdminVolunteerListItemDto>>(volunteers);
        }

        // =========================================================
        // פרופיל מתנדב (מנהל או המתנדב עצמו)
        // =========================================================

        public async Task<VolunteerResponseDto?> GetByIdAsync(int id, CurrentUserDto currentUser)
        {
            var volunteer = await LoadVolunteerForCallerAsync(id, currentUser);
            if (volunteer == null)
                return null;

            var volunteerDto = _mapper.Map<VolunteerResponseDto>(volunteer);

            // מי שאינו מנהל רואה רק את 3 הספרות האחרונות של תעודת הזהות
            if (!currentUser.IsAdmin && !string.IsNullOrEmpty(volunteerDto.IdNumber))
            {
                volunteerDto.IdNumber = "***-***-" + volunteerDto.IdNumber.Substring(Math.Max(0, volunteerDto.IdNumber.Length - 3));
            }

            return volunteerDto;
        }

        public async Task UpdateAsync(int id, UpdateVolunteerDto dto, CurrentUserDto currentUser)
        {
            var volunteer = await LoadVolunteerForCallerAsync(id, currentUser)
                            ?? throw new NotFoundException(VolunteerNotFound);

            // מתנדב שנמצא באמצע מסלול לא יכול לסמן את עצמו "פנוי" (הוא מסומן Busy עד סיום המסלול)
            if (dto.IsAvailable && !volunteer.IsAvailable &&
                await _routeRepository.GetActiveForVolunteerAsync(volunteer.Id) is { Status: RouteStatus.InProgress })
                throw new ConflictException("לא ניתן לסמן זמינות בזמן מסלול פעיל.");

            bool locationChanged = volunteer.CurrentLatitude != dto.CurrentLatitude || volunteer.CurrentLongitude != dto.CurrentLongitude;

            _mapper.Map(dto, volunteer);
            if (locationChanged && (dto.CurrentLatitude != 0 || dto.CurrentLongitude != 0))
                volunteer.LocationUpdatedAt = DateTime.UtcNow;

            await _volunteerRepository.UpdateAsync(volunteer);
        }

        public async Task<VolunteerResponseDto> CreateAsync(int userId, CreateVolunteerDto dto)
        {
            var user = await _userRepository.GetByIdAsync(userId)
                       ?? throw new UnauthorizedException("המשתמש המחובר לא נמצא.");

            if (await _volunteerRepository.GetByUserIdAsync(userId) != null)
                throw new ConflictException("למשתמש זה כבר קיים פרופיל מתנדב.");

            var volunteerEntity = _mapper.Map<Volunteer>(dto);
            volunteerEntity.UserId = userId;
            volunteerEntity.IdNumber = IsraeliId.Normalize(dto.IdNumber) ?? dto.IdNumber;
            volunteerEntity.ApprovalStatus = ApprovalStatus.Pending;
            if (dto.CurrentLatitude != 0 || dto.CurrentLongitude != 0)
                volunteerEntity.LocationUpdatedAt = DateTime.UtcNow;

            var (passed, details) = _validation.ValidateVolunteer(dto.IdNumber, dto.DriverLicenseNumber, dto.DriverLicenseExpiry, dto.DateOfBirth);

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _volunteerRepository.AddAsync(volunteerEntity);
                await _approvals.AddPendingAsync(userId, FoodRescue.DTOs.RequestedRole.Volunteer, passed, details);

                if (!string.IsNullOrEmpty(user.Email))
                {
                    await _notifications.QueueEmailAsync(
                        user.Id,
                        user.Email,
                        "רישום למערכת הצלת מזון",
                        "שלום רב, בקשת ההצטרפות שלך כמתנדב התקבלה בהצלחה וממתינה לאישור מנהל.",
                        summary: "בקשת ההצטרפות התקבלה");
                }
            });

            return _mapper.Map<VolunteerResponseDto>(volunteerEntity);
        }

        public async Task<List<AvailabilitySlotResponseDto>> GetAvailabilityAsync(int id, CurrentUserDto currentUser)
        {
            var volunteer = await LoadVolunteerForCallerAsync(id, currentUser)
                            ?? throw new NotFoundException(VolunteerNotFound);

            var slots = await _availabilitySlotRepository.GetByVolunteerIdWithRegionsAsync(volunteer.Id);
            return _mapper.Map<List<AvailabilitySlotResponseDto>>(slots);
        }

        public async Task SaveAvailabilityAsync(int id, List<CreateAvailabilitySlotDto> slots, CurrentUserDto currentUser)
        {
            var volunteer = await LoadVolunteerForCallerAsync(id, currentUser)
                            ?? throw new NotFoundException(VolunteerNotFound);

            var newSlots = await BuildSlotsAsync(volunteer.Id, slots);
            await _availabilitySlotRepository.ReplaceForVolunteerAsync(volunteer.Id, newSlots);
        }

        // =========================================================
        // הרשמת מתנדב חדש + יצירת משתמש (פתוח לכולם)
        // =========================================================

        public async Task RegisterWithUserAsync(RegisterVolunteerDto dto)
        {
            string email = EmailNormalizer.Normalize(dto.Email);

            if (await _userRepository.GetByEmailAsync(email) != null)
                throw new ConflictException("כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו.");

            string tempPassword = TempPasswordGenerator.Generate();
            var (passed, details) = _validation.ValidateVolunteer(dto.IdNumber, dto.DriverLicenseNumber, dto.DriverLicenseExpiry, dto.DateOfBirth);

            // תוקן: המשתמש, הפרופיל ובקשת האישור נשמרים יחד או לא נשמרים בכלל.
            // בעבר, כשהפרופיל נכשל (למשל ערך ארוך מדי), המשתמש כבר נשמר והמייל שלו נשאר "תפוס".
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var user = new User
                {
                    Name = dto.Name.Trim(),
                    Email = email,
                    Phone = dto.Phone.Trim(),
                    Role = UserRole.Volunteer,
                    MustChangePassword = true,           // חובה להחליף את הסיסמה הזמנית בכניסה הראשונה
                    SecurityStamp = UserService.NewStamp()
                };
                user.PasswordHash = _passwordHasher.HashPassword(user, tempPassword);

                if (!await _userRepository.TryAddAsync(user))
                    throw new ConflictException("כבר קיים במערכת משתמש הרשום עם כתובת אימייל זו.");

                await _volunteerRepository.AddAsync(new Volunteer
                {
                    UserId = user.Id,
                    VehicleType = dto.VehicleType.Trim(),
                    MaxCapacityKg = (double)dto.MaxCapacityKg,
                    IdNumber = IsraeliId.Normalize(dto.IdNumber) ?? dto.IdNumber.Trim(),
                    DriverLicenseNumber = dto.DriverLicenseNumber.Trim(),
                    DriverLicenseExpiry = dto.DriverLicenseExpiry?.Date,
                    DateOfBirth = dto.DateOfBirth.Date,
                    ApprovalStatus = ApprovalStatus.Pending,
                });

                await _approvals.AddPendingAsync(user.Id, FoodRescue.DTOs.RequestedRole.Volunteer, passed, details);

                await _notifications.QueueEmailAsync(
                    user.Id,
                    user.Email,
                    "פרטי ההתחברות שלך למערכת הצלת מזון",
                    $"שלום {WebUtility.HtmlEncode(user.Name)},<br/>בקשת ההצטרפות שלך למערכת התקבלה בהצלחה.<br/>" +
                    $"הסיסמה הזמנית שלך היא: <b>{tempPassword}</b><br/>בכניסה הראשונה תתבקש/י לבחור סיסמה חדשה.",
                    summary: "נשלחו פרטי התחברות");
            });
        }

        // =========================================================
        // מיקום חי
        // =========================================================

        public async Task UpdateMyLocationAsync(int userId, double latitude, double longitude)
        {
            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
                throw new ArgumentException("קואורדינטות לא תקינות.");

            var volunteer = await _volunteerRepository.GetByUserIdAsync(userId)
                            ?? throw new NotFoundException(VolunteerNotFound);

            volunteer.CurrentLatitude = latitude;
            volunteer.CurrentLongitude = longitude;
            volunteer.LocationUpdatedAt = DateTime.UtcNow;
            await _volunteerRepository.UpdateAsync(volunteer);

            var route = await _routeRepository.GetActiveForVolunteerAsync(volunteer.Id);
            if (route != null)
            {
                await _notifications.PublishAsync(RealtimeGroups.Route(route.Id), RealtimeEvents.VolunteerLocation, new
                {
                    routeId = route.Id,
                    volunteerId = volunteer.Id,
                    latitude,
                    longitude,
                    at = volunteer.LocationUpdatedAt
                });
            }
        }

        // =========================================================
        // עזר
        // =========================================================

        // מנהל ניגש למתנדב לפי המזהה שביקש. מתנדב רגיל תמיד מקבל רק את הפרופיל של עצמו.
        // מחזיר null אם המתנדב לא קיים או לא שייך למשתמש המחובר.
        private async Task<Volunteer?> LoadVolunteerForCallerAsync(int requestedId, CurrentUserDto currentUser)
        {
            if (currentUser.IsAdmin)
                return await _volunteerRepository.GetByIdAsync(requestedId);

            if (currentUser.UserId == null)
                return null;

            // בדיקת בעלות על המזהה שהתקבל בכתובת: מתנדב עם המזהה הזה *וגם* של המשתמש המחובר
            return await _volunteerRepository.GetOwnedAsync(requestedId, currentUser.UserId.Value);
        }

        // ממיר את משבצות הזמינות שהתקבלו מהלקוח ל-Entities, כולל בדיקות.
        // תוקן: בעבר לא נבדק ששעת ההתחלה לפני שעת הסיום, ש-ToRegionId שונה מ-RegionId,
        // ושהאזורים קיימים (אזור שלא קיים החזיר 500 מה-FK במסד).
        private async Task<List<AvailabilitySlot>> BuildSlotsAsync(int volunteerId, List<CreateAvailabilitySlotDto>? slotDtos)
        {
            if (slotDtos == null)
                throw new ArgumentException("נתוני הזמינות ריקים.");

            if (slotDtos.Count > MaxSlots)
                throw new ArgumentException($"אפשר להגדיר עד {MaxSlots} משבצות זמינות.");

            var regionIds = slotDtos
                .SelectMany(s => new[] { s.RegionId, s.ToRegionId ?? 0 })
                .Where(id => id > 0)
                .Distinct();

            foreach (var regionId in regionIds)
            {
                if (!await _regionRepository.ExistsAsync(regionId))
                    throw new ArgumentException($"האזור שנבחר (מזהה {regionId}) אינו קיים.");
            }

            var result = new List<AvailabilitySlot>();

            for (int i = 0; i < slotDtos.Count; i++)
            {
                var dto = slotDtos[i];
                string label = $"משבצת {i + 1}";

                if (dto.Day < 0 || dto.Day > 6)
                    throw new ArgumentException($"{label}: יום בשבוע חייב להיות בין 0 (ראשון) ל-6 (שבת).");

                var startTime = ParseTime(dto.StartTime, $"{label}: פורמט שעת התחלה אינו תקין.");
                var endTime = ParseTime(dto.EndTime, $"{label}: פורמט שעת סיום אינו תקין.");

                if (startTime >= endTime)
                    throw new ArgumentException($"{label}: שעת ההתחלה חייבת להיות לפני שעת הסיום.");

                int? toRegionId = dto.ToRegionId is > 0 ? dto.ToRegionId : null;
                if (toRegionId == dto.RegionId)
                    throw new ArgumentException($"{label}: אזור היעד חייב להיות שונה מאזור המוצא (להשאיר ריק לזמינות באזור אחד).");

                result.Add(new AvailabilitySlot
                {
                    VolunteerId = volunteerId,
                    RegionId = dto.RegionId,
                    ToRegionId = toRegionId,
                    Day = (DayOfWeek)dto.Day,
                    StartTime = startTime,
                    EndTime = endTime
                });
            }

            return result;
        }

        private static TimeSpan ParseTime(string? value, string error)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                TimeSpan.TryParseExact(value.Trim(), new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var t) &&
                t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            {
                return t;
            }

            throw new ArgumentException(error);
        }
    }
}
