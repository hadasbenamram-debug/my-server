using BLL.Algoritems;
using BLL.Algorithms;
using BLL.Configuration;
using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using BLL.Repositories;
using BLL.Services;
using BLL.Mappings;
using DAL;
using DAL.Classes;
using FoodRescue.Hubs;
using FoodRescue.Middlewares;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
var builder = WebApplication.CreateBuilder(args);

// הגדרת אזור הזמן המקומי (Asia/Jerusalem כברירת מחדל) עבור כל הבדיקות התלויות בשעון קיר
BLL.Algorithms.LocalClock.Configure(builder.Configuration["MatchingPolicy:TimeZoneId"] ?? "Asia/Jerusalem");

// רישום Repositories
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
builder.Services.Configure<MatchingPolicyOptions>(
    builder.Configuration.GetSection(MatchingPolicyOptions.SectionName));
builder.Services.AddSignalR();
// רישום שירות ה-Google Maps עם כתובת הבסיס המעודכנת ל-Google Routes API
builder.Services.AddHttpClient<
    BLL.Integrations.GoogleMaps.IGoogleMapsDistanceService,
    BLL.Integrations.GoogleMaps.GoogleMapsDistanceService>(client =>
    {
        client.BaseAddress = new Uri("https://routes.googleapis.com/");
        client.Timeout = TimeSpan.FromSeconds(10);
    });

builder.Services.AddSignalR();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddScoped<DonationMatchingOrchestrator>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<FoodRescue.Security.CallerProfileService>();

// הוספת שירותי Swagger עם מניעת התנגשויות שמות (SchemaId)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddScoped<PriorityDecayScheduler>();
builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
});

// רישום שירותי הליבה של שכבת ה-BLL
builder.Services.AddScoped<VolunteerMatchScorer>();
builder.Services.AddScoped<VolunteerRankingService>();
builder.Services.AddScoped<BLL.Services.MatchCreationService>();
builder.Services.AddScoped<BLL.Services.AdminEscalationService>();
builder.Services.AddSingleton<DistanceMemoizationService>();
builder.Services.AddAutoMapper(
    cfg => { },
    typeof(BLL.Mappings.MappingProfile).Assembly
); 
builder.Services.AddScoped<CommentService>();

builder.Services.AddScoped<BacktrackingAssignmentService>(sp =>
    new BacktrackingAssignmentService(
        maxSearchRadiusKm: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MatchingPolicyOptions>>().Value.MaxSearchRadiusKm,
        sendRequestToVolunteer: async (volunteer, donation, targetNgo) =>
        {
            var matchCreationService = sp.GetService<BLL.Services.MatchCreationService>();
            var emailService = sp.GetService<IEmailService>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<BLL.Algorithms.BacktrackingAssignmentService>>();
            try
            {
                if (matchCreationService != null)
                {
                    var (success, _) = await matchCreationService.CreateMatchAsync(donation, targetNgo, volunteer);
                    if (!success)
                    {
                        logger?.LogWarning("BacktrackingAssignmentService: CreateMatchAsync failed for donation {DonationId}", donation?.Id);
                        return;
                    }
                }

                if (volunteer?.User?.Email != null && emailService != null)
                {
                    await emailService.SendEmailAsync(
                        volunteer.UserId,
                        volunteer.User.Email,
                        "New donation available",
                        $"A donation (id={donation?.Id}) is available for pickup. Please respond in the app.");
                }
            }
            catch (System.Exception ex)
            {
                logger?.LogError(ex, "BacktrackingAssignmentService: failed to send notification to volunteer {VolunteerId}", volunteer?.Id);
            }
        },
        triggerEscalation: async (donation, outcome) =>
        {
            var escalationService = sp.GetRequiredService<BLL.Services.AdminEscalationService>();
            await escalationService.NotifyAdminsAsync(donation, outcome);
        }));

builder.Services.AddScoped<IRouteOptimizationService, RouteOptimizationService>();
builder.Services.AddScoped<KnapsackSolver>();
builder.Services.AddScoped<RegionAvailabilityMatchingService>();
builder.Services.AddScoped<CommentTreeService>();

builder.Services.AddScoped<BLL.Interfaces.IDonationAssignmentService, DonationAssignmentService>(sp =>
    new DonationAssignmentService(
        sp.GetRequiredService<RegionAvailabilityMatchingService>(),
        sp.GetRequiredService<VolunteerRankingService>(),
        sp.GetRequiredService<BacktrackingAssignmentService>(),
        sp.GetRequiredService<IVolunteerRepository>(),
        sp.GetRequiredService<DistanceMemoizationService>(),
        sp.GetService<IGoogleMapsDistanceService>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MatchingPolicyOptions>>().Value.MaxSearchRadiusKm));

builder.Services.AddScoped<PriorityHeapService<Donation, int>>(sp => new PriorityHeapService<Donation, int>(d => d.Id));

// רישום שירותי Background / Timeout
builder.Services.AddScoped<MatchTimeoutService>();
builder.Services.AddScoped<DonationMatchingBatchService>();
builder.Services.AddScoped<INgoSelectionService, NgoSelectionService>();
builder.Services.AddMemoryCache();

// רישום AutoMapper לפי ה-Assembly של המחלקה MappingProfile
builder.Services.AddAutoMapper(cfg => { }, typeof(MappingProfile));

builder.Services.AddDbContext<FoodRescueDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Hangfire configuration
builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHangfireServer();

// Register JWT authentication
var jwtSection = builder.Configuration.GetSection("JwtSettings");
var jwtSecret = jwtSection["Secret"];
if (string.IsNullOrEmpty(jwtSecret))
{
    throw new InvalidOperationException("JwtSettings:Secret is not configured. Set it via user-secrets or environment variables.");
}
if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException("JwtSettings:Secret must be at least 32 bytes long (use a random 256-bit value).");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };

        // הוספת חילוץ הטוקן מתוך שורת הכתובת עבור חיבורי SignalR
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/notificationHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
// הגדרת CORS מותאמת כולל AllowCredentials עבור SignalR ובקשות מאומתות
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            policy.WithOrigins("http://localhost:5174")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials(); // חובה לעבודה תקינה עם SignalR ו-Credentials
        });
});

var app = builder.Build();

app.UseCors("AllowReactApp");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FoodRescue API V1");
    });
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FoodRescueDbContext>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    string adminEmail = configuration["AdminSeed:Email"] ?? "admin@foodrescue.com";
    string adminPassword = configuration["AdminSeed:Password"] ?? "DefaultSecurePassword123!";
    string adminName = configuration["AdminSeed:Name"] ?? "מנהל ראשי";
    string adminPhone = configuration["AdminSeed:Phone"] ?? "0500000000";
    string adminDepartment = configuration["AdminSeed:Department"] ?? "הנהלה ראשית";

    var existingUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);

    if (existingUser == null)
    {
        existingUser = new User
        {
            Name = adminName,
            Email = adminEmail,
            Phone = adminPhone,
            PasswordHash = BLL.Algorithms.PasswordHasher.Hash(adminPassword),
            Role = UserRole.Admin,
            IsEmailVerified = true,
            IsPhoneVerified = true,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(existingUser);
        await dbContext.SaveChangesAsync();
    }

    var existingManager = await dbContext.Managers.AnyAsync(m => m.UserId == existingUser.Id);
    if (!existingManager)
    {
        var managerProfile = new Manager
        {
            UserId = existingUser.Id,
            Department = adminDepartment
        };

        dbContext.Managers.Add(managerProfile);
        await dbContext.SaveChangesAsync();
    }
}

// הפעלת טיפול גלובלי בשגיאות
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<NotificationHub>("/notificationHub");
using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<Hangfire.IRecurringJobManager>();

    recurringJobManager.AddOrUpdate<BLL.Algorithms.PriorityDecayScheduler>(
        "priority-decay-scheduler",
        service => service.ProcessPriorityDecay(),
        "*/2 * * * *");

    recurringJobManager.AddOrUpdate<BLL.Algorithms.DonationMatchingBatchService>(
        "hourly-donation-matching",
        service => service.RunBatchAsync(),
        "*/2 * * * *");

    RecurringJob.AddOrUpdate<MatchTimeoutService>(
        "process-match-timeouts",
        service => service.ProcessTimeoutsAndNoShowsAsync(DateTime.UtcNow),
        Cron.Minutely);
}

if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard();
}
else
{
    app.UseWhen(context => context.User?.IsInRole("Admin") == true, appBuilder => appBuilder.UseHangfireDashboard());
}

app.MapControllers();


app.Run();