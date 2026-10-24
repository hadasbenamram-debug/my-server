using BLL.Algorithms;
using BLL.Algoritems;
using BLL.Configuration;
using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using BLL.Mappings;
using BLL.Notifications;
using BLL.Security;
using BLL.Services;
using DAL;
using DAL.Interfaces;
using DAL.Repositories;
using FoodRescue.Hubs;
using FoodRescue.Infrastructure;
using FoodRescue.Middlewares;
using FoodRescue.Extensions;
using FoodRescue.Security;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// הגדרות כלליות
// =========================================================

// אזור זמן: התרומות שייכות ליום המקומי (ישראל), במסד הכל נשמר ב-UTC
LocalClock.Configure(builder.Configuration["MatchingPolicy:TimeZoneId"] ?? "Asia/Jerusalem");

builder.Services.AddSingleton(TimeProvider.System);

// =========================================================
// Repositories (DAL) - גישה לנתונים בלבד.
// ה-API מפנה ל-DAL רק בשביל הרישום הזה ל-DI; הקונטרולרים עצמם לא משתמשים ב-Repositories.
// =========================================================

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IDonationRepository, DonationRepository>();
builder.Services.AddScoped<IMatchRepository, MatchRepository>();
builder.Services.AddScoped<IVolunteerRepository, VolunteerRepository>();
builder.Services.AddScoped<INgoRepository, NgoRepository>();
builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
builder.Services.AddScoped<IFoodCategoryRepository, FoodCategoryRepository>();
builder.Services.AddScoped<IRegionRepository, RegionRepository>();
builder.Services.AddScoped<IApprovalRequestRepository, ApprovalRequestRepository>();
builder.Services.AddScoped<IManagerRepository, ManagerRepository>();
builder.Services.AddScoped<IAvailabilitySlotRepository, AvailabilitySlotRepository>();
builder.Services.AddScoped<ICommentRepository, CommentRepository>();
builder.Services.AddScoped<IDeliveryLogRepository, DeliveryLogRepository>();
builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IVolunteerRouteRepository, VolunteerRouteRepository>();
builder.Services.AddScoped<IActionTokenRepository, ActionTokenRepository>();
builder.Services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
builder.Services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// =========================================================
// מדיניות השידוך: ברירות מחדל מ-appsettings + שינויים של מנהל מהמסד (api/settings)
// =========================================================

builder.Services.Configure<MatchingPolicyOptions>(
    builder.Configuration.GetSection(MatchingPolicyOptions.SectionName));
builder.Services.AddSingleton<IMatchingPolicyProvider, MatchingPolicyProvider>();
builder.Services.AddScoped<ISettingsService, SettingsService>();

// =========================================================
// Controllers, Swagger, SignalR
// =========================================================

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // שגיאות ולידציה באותו מבנה אחיד כמו כל שאר השגיאות: { status, message, code, errors }
        options.InvalidModelStateResponseFactory = context =>
            new BadRequestObjectResult(ApiError.FromModelState(context.ModelState, context.HttpContext.TraceIdentifier));
    });
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();
builder.Services.AddHttpContextAccessor();

// =========================================================
// Reverse proxy: כתובת ה-IP האמיתית של הלקוח (חשובה לנעילת התחברות לפי IP ולהגבלת קצב).
// כברירת מחדל סומכים רק על פרוקסי מקומי (127.0.0.1 / ::1); אפשר להוסיף ForwardedHeaders:KnownProxies.
// =========================================================

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
    {
        if (IPAddress.TryParse(proxy, out var address))
            options.KnownProxies.Add(address);
    }
});

// =========================================================
// Rate limiting
// =========================================================

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) => new ValueTask(new ApiError
    {
        Status = StatusCodes.Status429TooManyRequests,
        Code = "TooManyRequests",
        Message = "בוצעו יותר מדי בקשות. נסו שוב בעוד דקה."
    }.WriteAsync(context.HttpContext));

    // ניתוח תמונות (AI) - עד 5 בדקה למשתמש
    options.AddPolicy("image-analysis", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.GetUserId()?.ToString() ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // טופס צור קשר - פתוח לאורחים, לכן מוגבל לפי IP: עד 3 פניות ב-10 דקות
    options.AddPolicy("contact-form", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    // התחברות / הרשמה / איפוס סיסמה / קישורים מהמייל - עד 20 בקשות בדקה לכל IP
    // (בנוסף לנעילה לפי מייל+IP שנשמרת במסד)
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// =========================================================
// Google Maps Routes API
// מרחקים נשמרים במטמון (DistanceCacheMinutes, ברירת מחדל 3 שעות) לפי קואורדינטות מעוגלות,
// כך שהמנוע לא פונה ל-Google בכל ריצה (בעבר: ריצה כל 2 דקות ומטמון של 2 דקות).
// =========================================================

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RouteLegCache>();
builder.Services.AddHttpClient<IGoogleRoutesClient, GoogleRoutesClient>(client =>
{
    client.BaseAddress = new Uri("https://routes.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<IGoogleMapsDistanceService, GoogleMapsDistanceService>();

// =========================================================
// התראות: מייל / SMS נשמרים בתור (Outbox) ונשלחים ברקע עם ניסיונות חוזרים.
// גוף ההודעה נשמר מוצפן (AES-GCM) ונמחק אחרי השליחה.
// =========================================================

builder.Services.AddSingleton<IPayloadProtector, AesGcmPayloadProtector>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient<ISmsSender, SmsSender>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserNotificationService, UserNotificationService>();
builder.Services.AddScoped<NotificationDispatcher>();

// =========================================================
// אבטחה: טוקנים חד-פעמיים (איפוס סיסמה, קודים, קישורי תגובה) ונעילת התחברות - במסד ולא בזיכרון
// =========================================================

builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IActionTokenService, ActionTokenService>();
builder.Services.AddScoped<ILoginLockoutService, LoginLockoutService>();
// ה-PasswordHasher של Microsoft (עם תמיכה באימות hash-ים בפורמט הישן של הפרויקט)
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IPasswordHasher<DAL.Classes.User>, PasswordHasherWithLegacySupport>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISessionValidationService, SessionValidationService>();

builder.Services.AddAutoMapper(cfg => { }, typeof(MappingProfile).Assembly);

// =========================================================
// Services עסקיים (BLL) - הקונטרולרים עובדים רק מולם
// =========================================================

builder.Services.AddScoped<CallerProfileService>();
builder.Services.AddScoped<IDonationService, DonationService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<INgoService, NgoService>();
builder.Services.AddScoped<IVolunteerService, VolunteerService>();
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddScoped<IApprovalRequestService, ApprovalRequestService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<CommentTreeService>();
builder.Services.AddScoped<IFoodCategoryService, FoodCategoryService>();
builder.Services.AddScoped<IRegionService, RegionService>();
builder.Services.AddScoped<IMapService, MapService>();
builder.Services.AddScoped<IDeliveryLogService, DeliveryLogService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddScoped<ISupportService, SupportService>();
builder.Services.AddScoped<IAdminReportService, AdminReportService>();
builder.Services.AddScoped<IValidationService, ValidationService>();
builder.Services.AddScoped<AdminSeedService>();
builder.Services.AddScoped<FoodCategorySeedService>();
builder.Services.AddScoped<RegionSeedService>();

// =========================================================
// מנוע השידוכים - מסלול אחד (DonationMatchingBatchService) לריצה המתוזמנת, להפעלה ידנית,
// לשיבוץ ידני ולשיבוץ מחדש. (בעבר היו שלושה מסלולים שונים, אחד מהם הוזרק כאן כ-delegate.)
// =========================================================

builder.Services.AddSingleton<EngineRunState>();
builder.Services.AddSingleton<IEngineLock>(sp => new HangfireEngineLock(sp.GetRequiredService<JobStorage>()));
builder.Services.AddScoped<KnapsackSolver>();
builder.Services.AddScoped<IRouteOptimizationService, RouteOptimizationService>();
builder.Services.AddScoped<DonationMatchingOrchestrator>();
builder.Services.AddScoped<RegionAvailabilityMatchingService>();
builder.Services.AddScoped<VolunteerMatchScorer>();
builder.Services.AddScoped<VolunteerRankingService>();
builder.Services.AddScoped<BacktrackingAssignmentService>();
builder.Services.AddScoped<INgoSelectionService, NgoSelectionService>();
builder.Services.AddScoped<MatchCreationService>();
builder.Services.AddScoped<AdminEscalationService>();
builder.Services.AddScoped<RouteWorkflowService>();
builder.Services.AddScoped<VolunteerReliabilityService>();
builder.Services.AddScoped<DonationMatchingBatchService>();

// ג'ובים ברקע
builder.Services.AddScoped<MatchTimeoutService>();
builder.Services.AddScoped<PriorityDecayScheduler>();
builder.Services.AddScoped<MaintenanceJobs>();

// =========================================================
// Database
// =========================================================

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is missing. Set it with User Secrets " +
        "(dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\") " +
        "or with the environment variable ConnectionStrings__DefaultConnection.");

builder.Services.AddDbContext<FoodRescueDbContext>(options => options.UseSqlServer(connectionString));

// =========================================================
// קבצים: תמונות תרומה (ציבורי, /images) ורישיונות עסק (תיקייה פרטית מחוץ ל-wwwroot)
// =========================================================

var configuredLicensesFolder = builder.Configuration["LicensesFolder"];
if (string.IsNullOrWhiteSpace(configuredLicensesFolder))
    throw new InvalidOperationException("LicensesFolder is not configured.");

var licensesFolder = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configuredLicensesFolder));

builder.Services.AddScoped<ILicenseFileService>(sp =>
    new LicenseFileService(licensesFolder, sp.GetRequiredService<ILogger<LicenseFileService>>()));

var configuredImagesFolder = builder.Configuration["ImagesFolder"];
if (string.IsNullOrWhiteSpace(configuredImagesFolder))
    throw new InvalidOperationException("ImagesFolder is not configured.");

var imagesFolder = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configuredImagesFolder));
Directory.CreateDirectory(imagesFolder);

// ImageService שומר רק את נתיב התיקייה, שלא משתנה, ולכן מופע אחד לכל האפליקציה מספיק (Singleton).
// הבנאי מקבל את הנתיב כמחרוזת, וה-DI לא יודע לבד איזו מחרוזת לתת לו - לכן יוצרים את המופע בעצמנו.
builder.Services.AddSingleton<IImageService>(sp =>
    new ImageService(imagesFolder, sp.GetRequiredService<ILogger<ImageService>>()));
builder.Services.AddScoped<OrphanImageCleanupService>();

builder.Services.AddHttpClient<IImageAnalysisService, GeminiImageAnalysisService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// =========================================================
// Hangfire
// =========================================================

builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseSqlServerStorage(connectionString));

builder.Services.AddHangfireServer();

// =========================================================
// JWT Authentication
// =========================================================

// הערכים שאינם סודיים (Issuer, Audience, ExpiresMinutes) ב-appsettings.json. המפתח: משתנה סביבה Jwt__Key או User Secrets.
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSection["Key"];

if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException("Jwt:Key is missing. Set the environment variable Jwt__Key, or in development: dotnet user-secrets set \"Jwt:Key\" \"<random value of at least 32 characters>\".");

if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes long (HS256 needs a 256-bit key). Use a long random value.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            // ברירת המחדל היא 5 דקות נוספות אחרי פקיעת הטוקן - מבטלים.
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            // SignalR שולח את הטוקן ב-query string (WebSocket לא תומך בכותרות)
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/notificationHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },

            // חותמת אבטחה (טוקנים ישנים אחרי שינוי סיסמה נדחים), תפקיד עדכני מהמסד, וסימון סיסמה זמנית
            OnTokenValidated = SecurityStampValidator.ValidateAsync
        };
    });

// =========================================================
// Frontend links + CORS (כתובות מתוך הגדרות, לא בקוד)
// =========================================================

if (string.IsNullOrWhiteSpace(builder.Configuration["App:FrontendBaseUrl"]))
    throw new InvalidOperationException("App:FrontendBaseUrl is not configured (e.g. App__FrontendBaseUrl=https://app.example.com).");

var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .Select(o => o.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (allowedOrigins.Length == 0)
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins is empty. Configure at least one origin " +
        "(appsettings.Development.json, or env vars Cors__AllowedOrigins__0, Cors__AllowedOrigins__1, ...).");
}

foreach (var origin in allowedOrigins)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
        origin == "*")
    {
        throw new InvalidOperationException($"Cors:AllowedOrigins contains an invalid origin: '{origin}'.");
    }
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              // סך הרשומות ברשימות מעומדות
              .WithExposedHeaders(FoodRescue.Controllers.ApiControllerBase.TotalCountHeader);
    });
});

// =========================================================
// Build
// =========================================================

var app = builder.Build();

app.UseForwardedHeaders();

// טיפול מרכזי בשגיאות - ראשון בצינור, כדי לתפוס שגיאות מכל השלבים שאחריו
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();

// רק תמונות התרומות, מתיקיית Images שברמת ה-Solution, בכתובת /images. אין קבצים ציבוריים ב-wwwroot.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imagesFolder),
    RequestPath = "/images"
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "FoodRescue API V1"));
}

app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

// =========================================================
// אתחול: מנהל ראשי, קטגוריות ואזורים בסיסיים, ומדיניות השידוך מהמסד
// =========================================================

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AdminSeedService>().SeedAdminAsync();
    await scope.ServiceProvider.GetRequiredService<FoodCategorySeedService>().SeedCategoriesAsync();
    await scope.ServiceProvider.GetRequiredService<RegionSeedService>().SeedRegionsAsync();
}

await app.Services.GetRequiredService<IMatchingPolicyProvider>().ReloadAsync();

// =========================================================
// SignalR
// =========================================================

app.MapHub<NotificationHub>("/notificationHub");

// =========================================================
// Hangfire recurring jobs
// תוקן: ה-timeout רשם DateTime.UtcNow פעם אחת בזמן הרישום (הערך "קפא"), כך ששידוכים לא פגו אף פעם.
// עכשיו כל ריצה לוקחת את השעה הנוכחית בעצמה.
// =========================================================

var policy = app.Services.GetRequiredService<IMatchingPolicyProvider>().Current;
var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
var localZone = new RecurringJobOptions { TimeZone = LocalClock.Zone };

// המנוע: ברירת מחדל פעם בשעה (MatchingPolicy:EngineCron). הריצה נעולה (DisableConcurrentExecution + נעילה מבוזרת).
recurringJobs.AddOrUpdate<DonationMatchingBatchService>(
    "hourly-donation-matching", s => s.RunScheduledAsync(), policy.EngineCron, localZone);

recurringJobs.AddOrUpdate<MatchTimeoutService>(
    "process-match-timeouts", s => s.ProcessTimeoutsAsync(), Cron.Minutely());

recurringJobs.AddOrUpdate<MatchTimeoutService>(
    "process-no-shows", s => s.ProcessNoShowsAsync(), "*/5 * * * *");

recurringJobs.AddOrUpdate<MaintenanceJobs>(
    "expire-donations", s => s.ExpireDonationsAsync(), "*/10 * * * *");

recurringJobs.AddOrUpdate<PriorityDecayScheduler>(
    "priority-decay-scheduler", s => s.ProcessPriorityDecay(), "*/10 * * * *");

recurringJobs.AddOrUpdate<NotificationDispatcher>(
    "notification-sweep", s => s.SweepAsync(), Cron.Minutely());

recurringJobs.AddOrUpdate<OrphanImageCleanupService>(
    "orphan-image-cleanup", s => s.CleanupAsync(), Cron.Hourly());

recurringJobs.AddOrUpdate<MaintenanceJobs>(
    "maintenance-cleanup", s => s.CleanupAsync(), Cron.Daily(3), localZone);

// Dashboard - Admin בלבד, בכל הסביבות (כולל Development).
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAdminAuthorizationFilter() }
});

app.MapControllers();

app.Run();

// נחשף לבדיקות אינטגרציה (WebApplicationFactory)
public partial class Program { }
