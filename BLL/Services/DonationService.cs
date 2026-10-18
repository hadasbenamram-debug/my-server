using AutoMapper;
using BLL.Algorithms;
using BLL.Configuration;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Notifications;
using BLL.Security;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApprovalStatus = DAL.Classes.ApprovalStatus;

namespace BLL.Services
{
    // Service עסקי של תרומות: הרשאות, ולידציות, חישובי עדיפות, תיאום עם שירותי התמונה, והמרה ל-DTO.
    public class DonationService : IDonationService
    {
        private const string ImagesUrlPrefix = "/images/";
        private const string DonationNotFound = "התרומה לא נמצאה.";
        private static readonly TimeSpan UploadedImageLifetime = TimeSpan.FromMinutes(30);

        private readonly IDonationRepository _donationRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly CallerProfileService _caller;
        private readonly IImageService _imageService;
        private readonly IImageAnalysisService _imageAnalysisService;
        private readonly IFoodCategoryRepository _categoryRepository;
        private readonly IRegionRepository _regionRepository;
        private readonly IActionTokenService _actionTokens;
        private readonly RouteWorkflowService _workflow;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMatchingPolicyProvider _policy;
        private readonly IMapper _mapper;

        public DonationService(
            IDonationRepository donationRepository,
            ISupplierRepository supplierRepository,
            INgoRepository ngoRepository,
            IMatchRepository matchRepository,
            CallerProfileService caller,
            IImageService imageService,
            IImageAnalysisService imageAnalysisService,
            IFoodCategoryRepository categoryRepository,
            IRegionRepository regionRepository,
            IActionTokenService actionTokens,
            RouteWorkflowService workflow,
            IUnitOfWork unitOfWork,
            IMatchingPolicyProvider policy,
            IMapper mapper)
        {
            _donationRepository = donationRepository;
            _supplierRepository = supplierRepository;
            _ngoRepository = ngoRepository;
            _matchRepository = matchRepository;
            _caller = caller;
            _imageService = imageService;
            _imageAnalysisService = imageAnalysisService;
            _categoryRepository = categoryRepository;
            _regionRepository = regionRepository;
            _actionTokens = actionTokens;
            _workflow = workflow;
            _unitOfWork = unitOfWork;
            _policy = policy;
            _mapper = mapper;
        }

        // =========================================================
        // ניתוח תמונת תרומה
        // =========================================================

        public async Task<ImageAnalysisResponseDto> AnalyzeImageAsync(int userId, IFormFile image, CancellationToken cancellationToken)
        {
            if (image == null || image.Length == 0)
                throw new ArgumentException("יש לצרף קובץ תמונה תקין.");

            string uniqueFileName;

            try
            {
                uniqueFileName = await _imageService.Save(image, cancellationToken);
            }
            catch (ImageFormatException)
            {
                throw new ArgumentException("הקובץ שהועלה אינו תמונה תקינה.");
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is InvalidOperationException)
            {
                throw new IOException("לא ניתן לשמור את התמונה כרגע.", ex);
            }

            // CreateAsync תבדוק שהתמונה הועלתה על ידי אותו משתמש. נשמר במסד (לא ב-MemoryCache).
            await _actionTokens.RegisterOwnedValueAsync(userId, ActionTokenPurpose.ImageUpload, uniqueFileName, UploadedImageLifetime);

            // הקטגוריות נשלפות מהמסד ונשלחות ל-AI, כך שהמזהים שיחזור תמיד קיימים בטבלה.
            var categories = _mapper.Map<List<CategoryOptionDto>>(await _categoryRepository.GetAllAsync());

            var result = await _imageAnalysisService.AnalyzeAsync(image, categories, cancellationToken);

            return new ImageAnalysisResponseDto
            {
                FilePath = ImagesUrlPrefix + uniqueFileName,
                AiParsedData = result.AiParsedData,
                Message = result.Message
            };
        }

        // =========================================================
        // רשימות
        // =========================================================

        public async Task<List<SupplierDonationSummaryDto>> GetMySupplierDonationsAsync(int userId)
        {
            var supplier = await _supplierRepository.GetByUserIdAsync(userId)
                           ?? throw new NotFoundException("לא נמצא פרופיל ספק עבור משתמש זה.");

            var donations = await _donationRepository.GetBySupplierIdWithRegionAsync(supplier.Id);
            return _mapper.Map<List<SupplierDonationSummaryDto>>(donations);
        }

        public async Task<IEnumerable<DonationResponseDto>> GetMyNgoDonationsAsync(CurrentUserDto currentUser)
        {
            if (currentUser.IsAdmin)
            {
                var allDonations = await _donationRepository.GetAllWithCategoriesAsync();
                return _mapper.Map<List<DonationResponseDto>>(allDonations);
            }

            var ngo = await _ngoRepository.GetByUserIdAsync(currentUser.UserId!.Value)
                      ?? throw new NotFoundException("לא נמצא פרופיל עמותה המשויך למשתמש זה.");

            // התרומות שהעמותה היא היעד שלהן, עם פרטי השידוך (סטטוס, מתנדב) - כדי לאשר קבלה ולדרג
            var matches = await _matchRepository.GetLatestForNgoAsync(ngo.Id);
            return matches.Select(m => WithMatch(_mapper.Map<DonationResponseDto>(m.Donation), m)).ToList();
        }

        public async Task<PagedResult<AdminDonationSummaryDto>> GetAllForAdminAsync(PageRequest page)
        {
            var result = await _donationRepository.GetPageWithSupplierAsync(page.Skip, page.PageSize);
            return new PagedResult<AdminDonationSummaryDto>(_mapper.Map<List<AdminDonationSummaryDto>>(result.Items), result.TotalCount);
        }

        public async Task<DonationResponseDto?> GetByIdAsync(int donationId, CurrentUserDto currentUser)
        {
            var donation = await _donationRepository.GetByIdWithCategoriesAsync(donationId);
            if (donation == null)
                return null;

            var caller = await _caller.GetAsync(currentUser);
            var match = await _matchRepository.GetCurrentForDonationAsync(donationId);

            // בדיקת בעלות: מחזירים null (= "לא נמצא") כדי לא לחשוף שהתרומה קיימת.
            bool allowed = caller.IsAdmin ||
                           caller.OwnsDonation(donation) ||
                           caller.IsApprovedVolunteer ||
                           (match != null && caller.IsMatchNgo(match));
            if (!allowed)
                return null;

            return WithMatch(_mapper.Map<DonationResponseDto>(donation), match);
        }

        public async Task<IEnumerable<DonationResponseDto>> GetAvailableAsync(CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);

            if (!caller.IsAdmin && !caller.IsApprovedVolunteer)
                throw new ForbiddenException("רק מנהל או מתנדב מאושר יכולים לצפות בתרומות הזמינות.");

            var now = DateTime.UtcNow;
            var donations = await _donationRepository.GetAvailableDonationsAsync(now, LocalDayStartUtc(now));
            return _mapper.Map<List<DonationResponseDto>>(donations);
        }

        // =========================================================
        // יצירה
        // =========================================================

        public async Task<DonationResponseDto> CreateAsync(CreateDonationDto dto, CurrentUserDto currentUser)
        {
            if (dto.Categories == null || !dto.Categories.Any())
                throw new ArgumentException("יש לספק לפחות שורת קטגוריה אחת עבור התרומה.");

            int userId = currentUser.UserId!.Value;

            // מנהל יוצר תרומה בשם ספק שהוא בוחר; ספק יוצר תרומה בשם עצמו.
            Supplier supplier;

            if (currentUser.IsAdmin)
            {
                supplier = await _supplierRepository.GetByIdAsync(dto.SupplierId)
                           ?? throw new ArgumentException("הספק שצוין לא נמצא.");
            }
            else
            {
                supplier = await _supplierRepository.GetByUserIdAsync(userId)
                           ?? throw new ForbiddenException("רק משתמש עם פרופיל ספק יכול ליצור תרומה.");
            }

            if (supplier.ApprovalStatus != ApprovalStatus.Approved)
                throw new ForbiddenException("הספק טרם אושר על ידי מנהל, ולכן לא ניתן ליצור תרומות.");

            await ValidateCategoriesAsync(dto.Categories);

            // בדיקה לפני השמירה: אזור שלא קיים היה מפיל את ה-INSERT על מפתח זר (500).
            if (!await _regionRepository.ExistsAsync(dto.RegionId))
                throw new ArgumentException("האזור שנבחר אינו קיים.");

            // תוקן: בעבר נכתב כאן UtcNow.AddHours(3), כך ששעת היצירה נשמרה 3 שעות קדימה,
            // בזמן ששאר המערכת עובדת ב-UTC (וההמרה לשעון מקומי נעשית ב-LocalClock).
            var now = DateTime.UtcNow;
            var (pickupTime, expiration) = ValidateTimes(dto.LatestPickupTime, dto.ExpirationTime, now);

            var donationEntity = _mapper.Map<Donation>(dto);

            // תמונה מתקבלת רק אם הועלתה על ידי המשתמש הנוכחי דרך AnalyzeImageAsync (ועדיין בתוקף)
            string safeImagePath = string.Empty;
            if (!string.IsNullOrWhiteSpace(dto.ImageFilePath))
            {
                var requestedFileName = ParseImageFileName(dto.ImageFilePath);
                if (!await _actionTokens.TryUseOwnedValueAsync(userId, ActionTokenPurpose.ImageUpload, requestedFileName, consume: true))
                    throw new ArgumentException("התמונה אינה תקפה או שלא הועלתה על ידי המשתמש הנוכחי. יש להעלות אותה מחדש.");

                safeImagePath = ImagesUrlPrefix + requestedFileName;
            }

            // השלמת נתונים שהשרת קובע (לא הלקוח)
            donationEntity.Id = 0;
            donationEntity.ImageFilePath = safeImagePath;
            donationEntity.GooglePlaceId = string.IsNullOrWhiteSpace(dto.GooglePlaceId) ? "N/A" : dto.GooglePlaceId.Trim();
            donationEntity.SupplierId = supplier.Id;
            donationEntity.CreatedAt = now;
            donationEntity.LatestPickupTime = pickupTime;
            donationEntity.ExpirationTime = expiration;
            donationEntity.Status = DonationStatus.Pending;
            ApplyPriority(donationEntity, now);

            // שמירה דרך ה-Repository. שורות הקטגוריות (DonationFoodCategories) נשמרות יחד עם התרומה.
            await _donationRepository.AddAsync(donationEntity);

            var createdDonation = await _donationRepository.GetByIdWithCategoriesAsync(donationEntity.Id);
            return _mapper.Map<DonationResponseDto>(createdDonation);
        }

        // =========================================================
        // עריכה / ביטול / אישור קבלה
        // =========================================================

        public async Task<DonationResponseDto> UpdateAsync(int donationId, UpdateDonationDto dto, CurrentUserDto currentUser)
        {
            var donation = await LoadOwnedForUpdateAsync(donationId, currentUser);

            if (donation.Status != DonationStatus.Pending)
                throw new ConflictException("אפשר לערוך תרומה רק כל עוד היא ממתינה לשיבוץ. אפשר לבטל אותה ולהזין חדשה.");

            await ValidateCategoriesAsync(dto.Categories);

            var now = DateTime.UtcNow;

            // היום של התרומה הוא יום היצירה שלה (תרומה מיועדת לאותו יום בלבד)
            var (pickupTime, expiration) = ValidateTimes(dto.LatestPickupTime, dto.ExpirationTime, now, donation.CreatedAt);

            donation.LatestPickupTime = pickupTime;
            donation.ExpirationTime = expiration;

            // עדכון השורות במקום (אותו מפתח DonationId+CategoryId), מחיקת שורות שהוסרו והוספת חדשות
            var existing = donation.DonationFoodCategories ?? new List<DonationFoodCategory>();
            foreach (var row in existing.Where(r => dto.Categories.All(c => c.CategoryId != r.CategoryId)).ToList())
                existing.Remove(row);

            foreach (var line in dto.Categories)
            {
                var row = existing.FirstOrDefault(r => r.CategoryId == line.CategoryId);
                if (row == null)
                {
                    existing.Add(new DonationFoodCategory
                    {
                        DonationId = donation.Id,
                        CategoryId = line.CategoryId,
                        Quantity = line.Quantity,
                        WeightKg = line.WeightKg
                    });
                }
                else
                {
                    row.Quantity = line.Quantity;
                    row.WeightKg = line.WeightKg;
                }
            }

            ApplyPriority(donation, now);

            if (!await _donationRepository.TryUpdateAsync(donation))
                throw new ConflictException("התרומה עודכנה במקביל (למשל שובצה ממש עכשיו). רעננו ונסו שוב.");

            var updated = await _donationRepository.GetByIdWithCategoriesAsync(donation.Id);
            return _mapper.Map<DonationResponseDto>(updated);
        }

        public async Task CancelAsync(int donationId, CurrentUserDto currentUser, string? reason)
        {
            var donation = await LoadOwnedForUpdateAsync(donationId, currentUser);

            if (donation.Status != DonationStatus.Pending && donation.Status != DonationStatus.Matched)
                throw new ConflictException("לא ניתן לבטל תרומה שכבר נאספה, נמסרה, פגה או בוטלה.");

            string message = string.IsNullOrWhiteSpace(reason)
                ? (currentUser.IsAdmin ? "התרומה בוטלה ע\"י מנהל." : "התרומה בוטלה ע\"י התורם.")
                : reason.Trim();

            bool saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                // שידוך פעיל (שעוד לא נאסף) מבוטל, והמתנדב מקבל הודעה
                await _workflow.CancelActiveMatchForDonationAsync(donation, message);

                donation.Status = DonationStatus.Cancelled;
                await _donationRepository.UpdateAsync(donation);
            });

            if (!saved)
                throw new ConflictException("שגיאת עדכון במקביל, נסה שוב.");
        }

        public async Task<int> ConfirmReceiptAsync(int donationId, CurrentUserDto currentUser)
        {
            var caller = await _caller.GetAsync(currentUser);
            if (caller.Ngo == null)
                throw new NotFoundException(DonationNotFound);

            var match = await _workflow.ConfirmReceiptAsync(donationId, caller.Ngo.Id);
            return match.Id;
        }

        // =========================================================
        // עזר
        // =========================================================

        // ספק - רק התרומות שלו; מנהל - כל תרומה. אחרת 404 (בלי לחשוף שהתרומה קיימת)
        private async Task<Donation> LoadOwnedForUpdateAsync(int donationId, CurrentUserDto currentUser)
        {
            var donation = await _donationRepository.GetForUpdateAsync(donationId)
                           ?? throw new NotFoundException(DonationNotFound);

            if (currentUser.IsAdmin)
                return donation;

            var caller = await _caller.GetAsync(currentUser);
            if (!caller.OwnsDonation(donation))
                throw new NotFoundException(DonationNotFound);

            return donation;
        }

        private async Task ValidateCategoriesAsync(List<DonationCategoryLineDto>? categories)
        {
            if (categories == null || categories.Count == 0)
                throw new ArgumentException("יש לספק לפחות שורת קטגוריה אחת עבור התרומה.");

            if (categories.Select(c => c.CategoryId).Distinct().Count() != categories.Count)
                throw new ArgumentException("אין לציין את אותה קטגוריה יותר מפעם אחת בתרומה.");

            var existingCategoryIds = (await _categoryRepository.GetAllAsync()).Select(c => c.Id).ToHashSet();
            if (categories.Any(c => !existingCategoryIds.Contains(c.CategoryId)))
                throw new ArgumentException("אחת הקטגוריות שנבחרו אינה קיימת.");
        }

        // שעת האיסוף האחרונה היא "שעון קיר" ביום התרומה; תאריך התפוגה - אם לא נשלח: סוף היום המקומי.
        private static (TimeSpan PickupTime, DateTime ExpirationUtc) ValidateTimes(
            TimeSpan latestPickupTime, DateTime? expirationTime, DateTime nowUtc, DateTime? createdAtUtc = null)
        {
            if (latestPickupTime < TimeSpan.Zero || latestPickupTime >= TimeSpan.FromDays(1))
                throw new ArgumentException("שעת האיסוף האחרונה אינה תקינה.");

            var donationDay = LocalClock.ToLocal(createdAtUtc ?? nowUtc).Date;
            var pickupUtc = LocalClock.ToUtc(donationDay + latestPickupTime);
            if (pickupUtc <= nowUtc)
                throw new ArgumentException("שעת האיסוף האחרונה כבר עברה. תרומה מיועדת לאותו יום בלבד.");

            var expirationUtc = expirationTime.HasValue
                ? ToUtcFromClient(expirationTime.Value)
                : LocalClock.ToUtc(donationDay.AddDays(1).AddMinutes(-1));

            if (expirationUtc <= nowUtc)
                throw new ArgumentException("תאריך התפוגה כבר עבר.");

            return (latestPickupTime, expirationUtc);
        }

        // ערך עם Z / היסט - כבר מוגדר. ערך בלי אזור זמן - מתפרש כשעון מקומי (Asia/Jerusalem).
        public static DateTime ToUtcFromClient(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => LocalClock.ToUtc(value)
        };

        private void ApplyPriority(Donation donation, DateTime nowUtc)
        {
            var policy = _policy.Current;
            donation.PriorityScore = PriorityCalculator.Calculate(donation, nowUtc, policy.UrgencyTimeConstantMinutes);
            donation.PriorityUpdatedAt = nowUtc;

            var hoursToExpiration = (donation.ExpirationTime - nowUtc).TotalHours;
            donation.UrgencyScore = hoursToExpiration > 0 ? Math.Round(Math.Min(100, 100 / hoursToExpiration), 2) : 100;
        }

        private static string ParseImageFileName(string imageFilePath)
        {
            if (!imageFilePath.StartsWith(ImagesUrlPrefix, StringComparison.Ordinal))
                throw new ArgumentException("נתיב התמונה אינו תקין.");

            var name = imageFilePath.Substring(ImagesUrlPrefix.Length);
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains('/') || name.Contains('\\'))
                throw new ArgumentException("נתיב התמונה אינו תקין.");

            return name;
        }

        private static DateTime LocalDayStartUtc(DateTime nowUtc) => LocalClock.ToUtc(LocalClock.ToLocal(nowUtc).Date);

        private static DonationResponseDto WithMatch(DonationResponseDto dto, Match? match)
        {
            if (match == null)
                return dto;

            dto.MatchId = match.Id;
            dto.MatchStatus = match.Status.ToString();
            dto.NgoId = match.NgoId;
            dto.NgoName = match.Ngo?.NgoName;
            dto.VolunteerId = match.VolunteerId;
            dto.VolunteerName = match.Volunteer?.User?.Name;
            dto.EstimatedArrival = match.EstimatedArrival;
            return dto;
        }
    }
}
