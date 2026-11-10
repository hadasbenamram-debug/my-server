using AutoMapper;
using BLL.Algorithms;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Notifications;
using BLL.Security;
using DAL;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using ApprovalStatus = DAL.Classes.ApprovalStatus;
using RequestedRole = DAL.Classes.RequestedRole;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace BLL.Services
{
    // תהליך האישור היחיד: כל מסלולי האישור/דחייה עוברים כאן.
    public class ApprovalRequestService : IApprovalRequestService
    {
        private const string RequestNotFound = "בקשת האישור לא נמצאה.";

        // קישור הכניסה במייל האישור: חד-פעמי, ותקף 3 ימים (אחר כך - התחברות רגילה עם מייל וסיסמה)
        public static readonly TimeSpan LoginLinkLifetime = TimeSpan.FromDays(3);

        private readonly IApprovalRequestRepository _approvalRequestRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IManagerRepository _managerRepository;
        private readonly IUserRepository _userRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IValidationService _validationService;
        private readonly INotificationService _notifications;
        private readonly IActionTokenService _actionTokens;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly ILogger<ApprovalRequestService> _logger;

        public ApprovalRequestService(
            IApprovalRequestRepository approvalRequestRepository,
            IVolunteerRepository volunteerRepository,
            ISupplierRepository supplierRepository,
            INgoRepository ngoRepository,
            IManagerRepository managerRepository,
            IUserRepository userRepository,
            IUnitOfWork unitOfWork,
            IValidationService validationService,
            INotificationService notifications,
            IActionTokenService actionTokens,
            IConfiguration configuration,
            IMapper mapper,
            ILogger<ApprovalRequestService> logger)
        {
            _approvalRequestRepository = approvalRequestRepository;
            _volunteerRepository = volunteerRepository;
            _supplierRepository = supplierRepository;
            _ngoRepository = ngoRepository;
            _managerRepository = managerRepository;
            _userRepository = userRepository;
            _unitOfWork = unitOfWork;
            _validationService = validationService;
            _notifications = notifications;
            _actionTokens = actionTokens;
            _configuration = configuration;
            _mapper = mapper;
            _logger = logger;
        }

        // =========================================================
        // קריאה
        // =========================================================

        public async Task<PagedResponse<ApprovalRequestResponseDto>> GetPageAsync(PageRequest page)
        {
            var result = await _approvalRequestRepository.GetPageAsync(page.Skip, page.PageSize);
            return new PagedResponse<ApprovalRequestResponseDto>
            {
                Items = _mapper.Map<List<ApprovalRequestResponseDto>>(result.Items),
                TotalCount = result.TotalCount
            };
        }

        public async Task<ApprovalRequestResponseDto?> GetByIdAsync(int id)
        {
            var request = await _approvalRequestRepository.GetByIdAsync(id);
            return request == null ? null : _mapper.Map<ApprovalRequestResponseDto>(request);
        }

        public async Task<List<PendingApprovalRequestDto>> GetPendingAsync(FoodRescue.DTOs.RequestedRole? role)
        {
            var requests = await _approvalRequestRepository.GetPendingWithUserAsync(role.HasValue ? ToEntityRole(role.Value) : null);
            var result = new List<PendingApprovalRequestDto>(requests.Count);

            foreach (var request in requests)
            {
                var dto = _mapper.Map<PendingApprovalRequestDto>(request);

                switch (request.RequestedRole)
                {
                    case RequestedRole.Volunteer:
                        var v = await _volunteerRepository.GetByUserIdAsync(request.UserId);
                        if (v == null) continue;           // בקשה בלי פרופיל - אין מה לאשר
                        dto.ProfileId = v.Id;
                        dto.ProfileName = request.User?.Name;
                        dto.VehicleType = v.VehicleType;
                        dto.MaxCapacityKg = v.MaxCapacityKg;
                        dto.DriverLicenseExpiry = v.DriverLicenseExpiry;
                        break;

                    case RequestedRole.Supplier:
                        var s = await _supplierRepository.GetByUserIdAsync(request.UserId);
                        if (s == null) continue;
                        dto.ProfileId = s.Id;
                        dto.ProfileName = s.BusinessName;
                        dto.Address = s.Address;
                        dto.TaxId = s.TaxId;
                        dto.HasLicenseFile = !string.IsNullOrWhiteSpace(s.BusinessLicenseFilePath);
                        break;

                    case RequestedRole.Ngo:
                        var n = await _ngoRepository.GetByUserIdAsync(request.UserId);
                        if (n == null) continue;
                        dto.ProfileId = n.Id;
                        dto.ProfileName = n.NgoName;
                        dto.Address = n.Address;
                        dto.RegistrationNumber = n.RegistrationNumber;
                        break;
                }

                result.Add(dto);
            }

            return result;
        }

        // =========================================================
        // אישור / דחייה
        // =========================================================

        public async Task ApproveRequestAsync(int requestId, int? managerUserId)
        {
            var request = await _approvalRequestRepository.GetByIdAsync(requestId)
                          ?? throw new NotFoundException(RequestNotFound);
            await DecideAsync(request.RequestedRole, request.UserId, request, approve: true, managerUserId, null);
        }

        public async Task RejectRequestAsync(int requestId, int? managerUserId, string? rejectionReason)
        {
            var request = await _approvalRequestRepository.GetByIdAsync(requestId)
                          ?? throw new NotFoundException(RequestNotFound);
            await DecideAsync(request.RequestedRole, request.UserId, request, approve: false, managerUserId, rejectionReason);
        }

        public async Task ApproveProfileAsync(FoodRescue.DTOs.RequestedRole dtoRole, int profileId, int? managerUserId)
        {
            var role = ToEntityRole(dtoRole);
            int userId = await GetProfileUserIdAsync(role, profileId);
            var request = await _approvalRequestRepository.GetLatestByUserAndRoleAsync(userId, role);
            await DecideAsync(role, userId, request, approve: true, managerUserId, null);
        }

        public async Task RejectProfileAsync(FoodRescue.DTOs.RequestedRole dtoRole, int profileId, int? managerUserId, string? rejectionReason)
        {
            var role = ToEntityRole(dtoRole);
            int userId = await GetProfileUserIdAsync(role, profileId);
            var request = await _approvalRequestRepository.GetLatestByUserAndRoleAsync(userId, role);
            await DecideAsync(role, userId, request, approve: false, managerUserId, rejectionReason);
        }

        // תוקן: חיפוש לפי מזהה הפרופיל בלבד. בעבר ספק חופש קודם לפי מזהה ספק ואחר כך לפי מזהה
        // משתמש, ושני המספרים מתחילים מ-1 - כך ש-approve/12 עלול היה לאשר עסק אחר.
        private async Task<int> GetProfileUserIdAsync(RequestedRole role, int profileId)
        {
            switch (role)
            {
                case RequestedRole.Volunteer:
                    return (await _volunteerRepository.GetByIdAsync(profileId))?.UserId
                           ?? throw new NotFoundException("המתנדב לא נמצא.");
                case RequestedRole.Supplier:
                    return (await _supplierRepository.GetByIdAsync(profileId))?.UserId
                           ?? throw new NotFoundException("הספק לא נמצא.");
                default:
                    return (await _ngoRepository.GetByIdAsync(profileId))?.UserId
                           ?? throw new NotFoundException("העמותה לא נמצאה.");
            }
        }

        private async Task DecideAsync(
            RequestedRole role, int userId, ApprovalRequest? request, bool approve, int? managerUserId, string? reason)
        {
            if (!approve && string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("יש לציין סיבת דחייה.");

            // מתנדב שרישיון הנהיגה שלו לא בתוקף לא מאושר (ממילא המנוע לא היה משבץ אותו)
            if (approve && role == RequestedRole.Volunteer)
            {
                var volunteer = await _volunteerRepository.GetByUserIdAsync(userId);
                if (volunteer != null && !DriverLicensePolicy.IsValid(volunteer.DriverLicenseExpiry, DateTime.UtcNow))
                    throw new ArgumentException("לא ניתן לאשר את המתנדב: " + DriverLicensePolicy.NotValidMessage +
                                                " אפשר לדחות את הבקשה ולבקש ממנו להירשם עם רישיון בתוקף.");
            }

            var manager = await GetOrCreateManagerAsync(managerUserId);
            var user = await _userRepository.GetByIdAsync(userId) ?? throw new NotFoundException("המשתמש המקושר לא נמצא.");
            var status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            var now = DateTime.UtcNow;

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                bool profileFound = await SetProfileStatusAsync(role, userId, status);
                if (!profileFound)
                    throw new NotFoundException("לא נמצא פרופיל מתאים לבקשה זו.");

                // פרופיל ישן בלי בקשת אישור - יוצרים אחת, כדי שתמיד יישאר תיעוד מי החליט ומתי
                if (request == null)
                {
                    request = new ApprovalRequest
                    {
                        UserId = userId,
                        RequestedRole = role,
                        CreatedAt = now,
                        ValidationDetails = string.Empty,
                        RejectionReason = string.Empty
                    };
                    await _approvalRequestRepository.AddAsync(request);
                }

                request.Status = status;
                request.HandledByManagerId = manager.Id;
                request.HandledAt = now;
                request.RejectionReason = approve ? string.Empty : reason!.Trim();
                await _approvalRequestRepository.UpdateAsync(request);

                // תוקן: עמותה קיבלה Role=User. עכשיו לכל סוגי הפרופילים אותו כלל - משתמש שאושר מקבל
                // את התפקיד של הפרופיל (משתמש קיים שהוסיף פרופיל מקבל אותו רק עם האישור).
                if (approve && user.Role == UserRole.User)
                {
                    user.Role = ToUserRole(role);
                    await _userRepository.UpdateAsync(user);
                }

                // חדש: במייל האישור יש קישור כניסה אישי וחד-פעמי, כך שהמשתמש נכנס ישר למסך הראשון
                // של התפקיד (למתנדב - הגדרת הזמינות) בלי לעבור קודם במסך ההתחברות.
                string? loginToken = approve
                    ? await _actionTokens.CreateLinkTokenAsync(user.Id, ActionTokenPurpose.LoginLink, LoginLinkLifetime, (int)role)
                    : null;

                await QueueDecisionEmailAsync(user, role, approve, reason, loginToken);
            });

            // תיקון 10: לחשבון יכולים להיות כמה תפקידים - בהודעה כתוב על איזה תפקיד מדובר
            await _notifications.NotifyUserAsync(
                user.Id,
                approve ? $"בקשת ההצטרפות שלך {RoleTitles.For(role)} אושרה" : $"בקשת ההצטרפות שלך {RoleTitles.For(role)} נדחתה",
                approve ? "אפשר להתחיל לפעול במערכת." : $"סיבה: {reason}",
                link: approve ? FrontendLinks.RoleHomePath(role) : null,
                type: approve ? "success" : "warning");
        }

        private async Task<bool> SetProfileStatusAsync(RequestedRole role, int userId, ApprovalStatus status)
        {
            switch (role)
            {
                case RequestedRole.Volunteer:
                    var volunteer = await _volunteerRepository.GetByUserIdAsync(userId);
                    if (volunteer == null) return false;
                    volunteer.ApprovalStatus = status;
                    await _volunteerRepository.UpdateAsync(volunteer);
                    return true;

                case RequestedRole.Supplier:
                    var supplier = await _supplierRepository.GetByUserIdAsync(userId);
                    if (supplier == null) return false;
                    supplier.ApprovalStatus = status;
                    await _supplierRepository.UpdateAsync(supplier);
                    return true;

                default:
                    var ngo = await _ngoRepository.GetByUserIdAsync(userId);
                    if (ngo == null) return false;
                    ngo.ApprovalStatus = status;
                    await _ngoRepository.UpdateAsync(ngo);
                    return true;
            }
        }

        // מייל ההחלטה. בעבר נשלח JWT מלא ל-3 שעות בתוך הכתובת, ואחר כך הקישור הוביל רק למסך ההתחברות.
        // עכשיו: טוקן אקראי חד-פעמי (במסד נשמר רק ה-Hash שלו) שמוחלף בשרת בכניסה אחת (POST api/auth/link-login).
        private async Task QueueDecisionEmailAsync(User user, RequestedRole role, bool approved, string? reason, string? loginToken)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                return;

            string subject;
            string body;

            if (approved)
            {
                string enterUrl = (loginToken == null
                                      ? null
                                      : FrontendLinks.TryBuild(_configuration, $"app/welcome?token={WebUtility.UrlEncode(loginToken)}"))
                                  ?? FrontendLinks.TryBuild(_configuration, "app/login")
                                  ?? "";
                string nextStep = role switch
                {
                    RequestedRole.Volunteer => "בלחיצה על הכפתור תיכנס/י ישר למסך הגדרת הזמינות - הימים והשעות שבהם תוכל/י להתנדב.",
                    RequestedRole.Ngo => "בלחיצה על הכפתור תיכנסו ישר למסך עדכון כתובת העמותה ושעות הקבלה.",
                    _ => "בלחיצה על הכפתור תיכנסו ישר למערכת ותוכלו להזין תרומות."
                };

                // תיקון 37: מייל מעוצב (EmailTemplate) עם כפתור כניסה
                subject = "בקשת ההצטרפות שלך אושרה - לתת באהבה";
                body = EmailTemplate.Greeting(user.Name) +
                       EmailTemplate.Paragraph($"שמחים לעדכן שבקשת ההצטרפות שלך {RoleTitles.For(role)} <b>אושרה!</b> ברוכים הבאים למשפחת {EmailTemplate.BrandName}.") +
                       EmailTemplate.Paragraph(nextStep) +
                       (string.IsNullOrEmpty(enterUrl) ? "" : EmailTemplate.Button(enterUrl, "כניסה למערכת")) +
                       (loginToken == null ? "" : EmailTemplate.Note("הקישור אישי, לשימוש אחד ותקף ל-3 ימים. אחר כך נכנסים כרגיל עם המייל והסיסמה.")) +
                       EmailTemplate.Signature();
            }
            else
            {
                subject = "עדכון לגבי בקשת ההצטרפות שלך - לתת באהבה";
                body = EmailTemplate.Greeting(user.Name) +
                       EmailTemplate.Paragraph($"לצערנו בקשת ההצטרפות שלך {RoleTitles.For(role)} לא אושרה.") +
                       EmailTemplate.Details(("סיבה", EmailTemplate.Encode(string.IsNullOrWhiteSpace(reason) ? "לא צוינה סיבה" : reason))) +
                       EmailTemplate.Paragraph("אם יש שאלה, או שהפרטים השתנו - אפשר לפנות אלינו דרך טופס צור קשר באתר.") +
                       EmailTemplate.Signature();
            }

            await _notifications.QueueEmailAsync(user.Id, user.Email, subject, body,
                summary: approved ? "בקשת ההצטרפות אושרה" : "בקשת ההצטרפות נדחתה");
        }

        private async Task<Manager> GetOrCreateManagerAsync(int? managerUserId)
        {
            if (managerUserId == null)
                throw new UnauthorizedException("לא זוהה המנהל המחובר.");

            var manager = await _managerRepository.GetByUserIdAsync(managerUserId.Value);
            if (manager != null)
                return manager;

            // כל משתמש בתפקיד Admin הוא מנהל. אם עוד לא נוצר לו פרופיל מנהל - יוצרים, כדי שההחלטה תתועד.
            var user = await _userRepository.GetByIdAsync(managerUserId.Value);
            if (user == null || user.Role != UserRole.Admin)
                throw new UnauthorizedException("לא נמצא פרופיל מנהל עבור המשתמש המחובר.");

            manager = new Manager { UserId = user.Id, Department = "ניהול" };
            await _managerRepository.AddAsync(manager);
            return manager;
        }

        // =========================================================
        // יצירת בקשות
        // =========================================================

        public async Task<ApprovalRequestResponseDto> CreateAsync(CreateApprovalRequestDto dto, int userId)
        {
            var role = (RequestedRole)(int)dto.RequestedRole;
            var (passed, details) = await _validationService.ValidateApplicantAsync(dto.IdNumber, dto.LicenseNumber);

            var requestEntity = new ApprovalRequest
            {
                // אבטחה: הבקשה משויכת למשתמש המחובר לפי ה-JWT, לא לפי הלקוח
                UserId = userId,
                RequestedRole = role,
                Status = ApprovalStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                PassedAutomaticValidation = passed,
                ValidationDetails = details,
                RejectionReason = string.Empty
            };

            await _approvalRequestRepository.AddAsync(requestEntity);
            return _mapper.Map<ApprovalRequestResponseDto>(requestEntity);
        }

        public Task AddPendingAsync(int userId, FoodRescue.DTOs.RequestedRole role, bool passedAutomaticValidation, string validationDetails) =>
            _approvalRequestRepository.AddAsync(new ApprovalRequest
            {
                UserId = userId,
                RequestedRole = ToEntityRole(role),
                Status = ApprovalStatus.Pending,
                PassedAutomaticValidation = passedAutomaticValidation,
                ValidationDetails = validationDetails ?? string.Empty,
                RejectionReason = string.Empty,
                CreatedAt = DateTime.UtcNow
            });

        // =========================================================
        // עזר
        // =========================================================

        // ה-enum של DTOs זהה בשמות ובערכים ל-enum של DAL. ההמרה נעשית כאן, ב-Service, ולא בקונטרולר.
        private static RequestedRole ToEntityRole(FoodRescue.DTOs.RequestedRole role) => (RequestedRole)(int)role;

        public static UserRole ToUserRole(RequestedRole role) => role switch
        {
            RequestedRole.Supplier => UserRole.Supplier,
            RequestedRole.Volunteer => UserRole.Volunteer,
            _ => UserRole.Ngo
        };
    }
}
