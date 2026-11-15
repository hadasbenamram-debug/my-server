using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Notifications;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BLL.Services
{
    // תיקון 77: מחיקת חשבון בידי המשתמש.
    // זו "מחיקה רכה": המשתמש לא נמחק מהמסד אלא מסומן כלא פעיל (IsActive = false), כי התרומות,
    // המסלולים, המשובים וההתראות שלו הם גם ההיסטוריה של הצדדים האחרים (ספק, עמותה, מתנדב).
    //
    // מה קורה במחיקה:
    //   1. הסיסמה הנוכחית נבדקת (מי שמצא מחשב פתוח לא ימחק את החשבון).
    //   2. אי אפשר למחוק באמצע עבודה: מסלול פעיל, תרומות בטיפול, או תרומות בדרך לעמותה.
    //   3. IsActive = false + תאריך מחיקה, וחותמת האבטחה מתחלפת - כל הטוקנים נפסלים מיד.
    //   4. מתנדב מסומן כלא זמין, כך שהמנוע לא ישבץ אותו (וגם השאילתה של המנוע בודקת IsActive).
    public sealed class AccountDeletionService : IAccountDeletionService
    {
        private const string ActiveRouteMessage =
            "יש לך בקשת איסוף או מסלול פעיל. יש לענות על הבקשה או לסיים את המסלול לפני מחיקת החשבון.";
        private const string OpenDonationsMessage =
            "יש לך תרומות שעדיין בטיפול. יש לחכות שיסתיימו או לבטל אותן לפני מחיקת החשבון.";
        private const string IncomingDonationsMessage =
            "יש תרומות שבדרך לעמותה או שממתינות לאישור קבלה. יש לאשר את קבלתן לפני מחיקת החשבון.";
        private const string AdminMessage =
            "חשבון של מנהל מערכת לא נמחק מהפרופיל. אפשר לפנות למנהל אחר.";

        // תיקון 79: נושא המייל שנשלח למשתמש אחרי שמנהל שחזר את החשבון (ציבורי - לבדיקות)
        public const string RestoredSubject = "החשבון שלך שוחזר - לתת באהבה";

        private readonly IUserRepository _users;
        private readonly IVolunteerRepository _volunteers;
        private readonly ISupplierRepository _suppliers;
        private readonly INgoRepository _ngos;
        private readonly IVolunteerRouteRepository _routes;
        private readonly IDonationRepository _donations;
        private readonly IMatchRepository _matches;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly INotificationService _notifications;   // תיקון 79: מייל "החשבון שוחזר"
        private readonly IConfiguration _configuration;          // תיקון 79: קישור הכניסה במייל
        private readonly ILogger<AccountDeletionService> _logger;
        private readonly TimeProvider _clock;

        public AccountDeletionService(
            IUserRepository users,
            IVolunteerRepository volunteers,
            ISupplierRepository suppliers,
            INgoRepository ngos,
            IVolunteerRouteRepository routes,
            IDonationRepository donations,
            IMatchRepository matches,
            IUnitOfWork unitOfWork,
            IPasswordHasher<User> passwordHasher,
            INotificationService notifications,
            IConfiguration configuration,
            ILogger<AccountDeletionService> logger,
            TimeProvider? clock = null)
        {
            _users = users;
            _volunteers = volunteers;
            _suppliers = suppliers;
            _ngos = ngos;
            _routes = routes;
            _donations = donations;
            _matches = matches;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _notifications = notifications;
            _configuration = configuration;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task DeleteMyAccountAsync(int userId, DeleteAccountDto dto)
        {
            var user = await _users.GetByIdAsync(userId);
            if (user == null || !user.IsActive)
                throw new UnauthorizedException("המשתמש לא נמצא.");

            if (string.IsNullOrEmpty(user.PasswordHash) ||
                _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password ?? string.Empty)
                    == PasswordVerificationResult.Failed)
                throw new ArgumentException("הסיסמה שגויה.");

            if (user.Role == UserRole.Admin)
                throw new ForbiddenException(AdminMessage);

            // לחשבון אחד יכולים להיות כמה פרופילים (מתנדב, ספק, עמותה) - בודקים את כולם
            var volunteer = await _volunteers.GetByUserIdAsync(userId);
            if (volunteer != null && await _routes.GetActiveForVolunteerAsync(volunteer.Id) != null)
                throw new ConflictException(ActiveRouteMessage);

            var supplier = await _suppliers.GetByUserIdAsync(userId);
            if (supplier != null && await _donations.HasOpenForSupplierAsync(supplier.Id))
                throw new ConflictException(OpenDonationsMessage);

            var ngo = await _ngos.GetByUserIdAsync(userId);
            if (ngo != null && await _matches.HasOpenForNgoAsync(ngo.Id))
                throw new ConflictException(IncomingDonationsMessage);

            var saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                user.IsActive = false;
                user.DeactivatedAt = _clock.GetUtcNow().UtcDateTime;
                user.SecurityStamp = UserService.NewStamp();      // כל הטוקנים של החשבון נפסלים מיד
                await _users.UpdateAsync(user);

                if (volunteer != null && volunteer.IsAvailable)
                {
                    volunteer.IsAvailable = false;
                    await _volunteers.UpdateAsync(volunteer);
                }
            });

            if (!saved)
                throw new ConflictException("החשבון עודכן בינתיים. יש לרענן את הדף ולנסות שוב.");

            _logger.LogInformation("User {UserId} deleted their account (soft delete).", userId);
        }

        // תיקון 78: שחזור חשבון בידי מנהל - ההפך מהמחיקה.
        // החשבון חוזר לפעול עם אותה סיסמה, אותם תפקידים ואותה היסטוריה (ציון האמינות של מתנדב נשמר).
        // מתנדב חוזר להיות זמין, כך שהמנוע יכול לשבץ אותו שוב לפי הזמינות השבועית שלו.
        public async Task RestoreAccountAsync(int userId)
        {
            var user = await _users.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("המשתמש לא נמצא.");
            if (user.IsActive)
                throw new ConflictException("החשבון הזה פעיל - אין מה לשחזר.");

            var volunteer = await _volunteers.GetByUserIdAsync(userId);

            var saved = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                user.IsActive = true;
                user.DeactivatedAt = null;
                await _users.UpdateAsync(user);

                if (volunteer != null && !volunteer.IsAvailable)
                {
                    volunteer.IsAvailable = true;
                    await _volunteers.UpdateAsync(volunteer);
                }
            });

            if (!saved)
                throw new ConflictException("החשבון עודכן בינתיים. יש לרענן את הדף ולנסות שוב.");

            _logger.LogInformation("User {UserId} account was restored by an admin.", userId);

            // תיקון 79: מייל למשתמש שהחשבון שלו שוחזר. נשלח אחרי שהשחזור נשמר.
            // תקלה בהכנסת המייל לתור לא מבטלת את השחזור - רק נרשמת ביומן.
            try
            {
                await QueueRestoredEmailAsync(user, isVolunteer: volunteer != null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Account {UserId} was restored, but the notification e-mail was not queued.", userId);
            }
        }

        // תיקון 79: "החשבון שלך שוחזר" - באותו עיצוב כמו שאר המיילים של האתר (EmailTemplate)
        private async Task QueueRestoredEmailAsync(User user, bool isVolunteer)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                return;

            var loginUrl = FrontendLinks.TryBuild(_configuration, "app/login");

            var body =
                EmailTemplate.Greeting(user.Name) +
                EmailTemplate.Paragraph($"שמחים לעדכן שהחשבון שלך <b>שוחזר</b>, לפי הבקשה שהגיעה אלינו.") +
                EmailTemplate.Paragraph("אפשר להתחבר שוב עם אותו מייל ואותה סיסמה. כל התפקידים וההיסטוריה שלך נשמרו כמו שהיו.") +
                (isVolunteer
                    ? EmailTemplate.Paragraph("בקשות איסוף יגיעו שוב לפי הזמינות השבועית שלך, ולכן כדאי לבדוק שהיא עדיין מעודכנת.")
                    : string.Empty) +
                (string.IsNullOrEmpty(loginUrl) ? string.Empty : EmailTemplate.Button(loginUrl, "כניסה למערכת")) +
                EmailTemplate.Note("שכחת את הסיסמה? בדף ההתחברות יש קישור \"שכחת סיסמה?\". " +
                                   "לא ביקשת לשחזר את החשבון? כדאי לפנות אלינו מיד דרך טופס צור קשר באתר.") +
                EmailTemplate.Signature();

            await _notifications.QueueEmailAsync(user.Id, user.Email, RestoredSubject, body, summary: "החשבון שוחזר");
        }
    }
}
