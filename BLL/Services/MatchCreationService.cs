using BLL.Interfaces;
using BLL.Algorithms;
using System.Collections.Generic;
using System.Linq;
using DAL;
using DAL.Classes;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Hangfire;
using BLL.Services;

namespace BLL.Services
{
    public class MatchCreationService
    {
        private readonly FoodRescueDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IUserRepository _userRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MatchCreationService> _logger;

        public MatchCreationService(
            FoodRescueDbContext context,
            IEmailService emailService,
            IUserRepository userRepository,
            IConfiguration configuration,
            ILogger<MatchCreationService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Create a Match and update Donation.Status. 
        /// Volunteer.IsAvailable is NOT set to false here anymore; it will be updated only when the volunteer accepts.
        /// </summary>
        public async Task<(bool Success, Match CreatedMatch)> CreateMatchAsync(Donation donation, Ngo ngo, Volunteer volunteer)
        {
            if (donation == null) throw new ArgumentNullException(nameof(donation));
            if (ngo == null) throw new ArgumentNullException(nameof(ngo));
            if (volunteer == null) throw new ArgumentNullException(nameof(volunteer));

            Match match;
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                match = await CreateSingleMatchRowAsync(donation, ngo, volunteer, null, null);

                donation.Status = DonationStatus.Matched;
                _context.Donations.Update(donation);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync();
                return (false, null);
            }

            // שליחת המייל והתזמון מתבצעים לאחר סיום והתחייבות הטרנזקציה בהצלחה
            await SendAssignmentEmailAndScheduleTimeoutAsync(volunteer, match);

            return (true, match);
        }

        public async Task<(bool Success, VolunteerRoute CreatedRoute)> CreateRouteMatchesAsync(
              Volunteer volunteer,
              Ngo ngo,
              List<PickupStop> route,
              IReadOnlyDictionary<int, Donation> donationsById,
              CancellationToken ct = default)
        {
            if (volunteer == null) throw new ArgumentNullException(nameof(volunteer));
            if (ngo == null) throw new ArgumentNullException(nameof(ngo));
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (donationsById == null) throw new ArgumentNullException(nameof(donationsById));

            VolunteerRoute vroute;
            var createdMatches = new List<Match>();

            using var tx = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                vroute = new VolunteerRoute
                {
                    VolunteerId = volunteer.Id,
                    ScheduledAt = DateTime.UtcNow,
                    TotalWeightKg = route.Select(s => donationsById.ContainsKey(s.DonationId) ? donationsById[s.DonationId].TotalWeightKg() : 0.0).Sum(),
                    Status = RouteStatus.Planned,
                    GoogleDirectionsPolyline = string.Empty
                };

                await _context.VolunteerRoutes.AddAsync(vroute, ct);
                await _context.SaveChangesAsync(ct);

                foreach (var stop in route)
                {
                    if (!donationsById.TryGetValue(stop.DonationId, out var donation))
                    {
                        continue;
                    }

                    // רענון התרומה מהמסד כדי לוודא שה-RowVersion מעודכן אחרי עדכון ה-PriorityScore
                    await _context.Entry(donation).ReloadAsync(ct);

                    var match = await CreateSingleMatchRowAsync(donation, ngo, volunteer, vroute.Id, stop.EstimatedArrival, ct);
                    createdMatches.Add(match);

                    donation.Status = DonationStatus.Matched;
                    _context.Donations.Update(donation);
                }

                await _context.SaveChangesAsync(ct);
                vroute.Matches = createdMatches;

                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(ct);
                return (false, null);
            }

            // שליחת מייל לכל שידוך שנוצר במסלול לאחר שהמסלול והשידוכים נשמרו בהצלחה במסד
            foreach (var match in createdMatches)
            {
                await SendAssignmentEmailAndScheduleTimeoutAsync(volunteer, match);
            }

            return (true, vroute);
        }

        private async Task<Match> CreateSingleMatchRowAsync(
            Donation donation,
            Ngo ngo,
            Volunteer volunteer,
            int? volunteerRouteId,
            DateTime? estimatedArrival,
            CancellationToken ct = default)
        {
            var match = new Match
            {
                DonationId = donation.Id,
                NgoId = ngo.Id,
                VolunteerId = volunteer.Id,
                VolunteerRouteId = volunteerRouteId,
                Status = MatchStatus.AwaitingVolunteerConfirmation,
                IsActive = true,
                AssignedAt = DateTime.UtcNow,
                EstimatedArrival = estimatedArrival
            };

            await _context.Matches.AddAsync(match, ct);
            return match;
        }

        /// <summary>
        /// פונקציית עזר פרטית לטיפול ביצירת טוקן JWT, שליחת האימייל למתנדב ותזמון ה-Timeout ב-Hangfire
        /// </summary>
        private async Task SendAssignmentEmailAndScheduleTimeoutAsync(Volunteer volunteer, Match createdMatch)
        {
            var user = await _userRepository.GetByIdAsync(volunteer.UserId);
            if (user == null)
            {
                _logger.LogWarning("Assigned volunteer ID {VolunteerId} has no corresponding User record (UserId: {UserId}). Email not sent.", volunteer.Id, volunteer.UserId);
                return;
            }

            try
            {
                var jwtSettings = _configuration.GetSection("JwtSettings");
                var secretKeyString = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret Key is missing in configuration.");
                var secretKey = Encoding.UTF8.GetBytes(secretKeyString);

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                    new Claim(ClaimTypes.Role, user.Role.ToString()),
                    new Claim("MatchId", createdMatch.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                };

                var key = new SymmetricSecurityKey(secretKey);
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var tokenDescriptor = new JwtSecurityToken(
                    issuer: jwtSettings["Issuer"],
                    audience: jwtSettings["Audience"],
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(5),
                    signingCredentials: creds
                );

                string matchToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
                var frontendBaseUrl = "http://localhost:5174";
                var responseLink = $"{frontendBaseUrl}/app/volunteer/incoming-request/{createdMatch.Id}?token={matchToken}";

                _logger.LogInformation("Attempting to send assignment email to User ID {UserId} ({Email}) for Match ID {MatchId}", user.Id, user.Email, createdMatch.Id);

                await _emailService.SendEmailAsync(
                    user.Id,
                    user.Email,
                    "תרומה חדשה ממתינה לאישורך - יש לך 5 דקות!",
                    $"שלום {System.Net.WebUtility.HtmlEncode(user.Name)}, נמצאה עבורך תרומה חדשה לאיסוף. לאישור או דחייה תוך 5 דקות לחץ על הקישור הבא: <a href='{responseLink}'>לחץ כאן לאישור/דחייה</a>"
                );

                _logger.LogInformation("Email successfully sent to User ID {UserId}", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send assignment email to User ID {UserId} for Match ID {MatchId}. Error: {Message}", user.Id, createdMatch.Id, ex.Message);
            }

            // תזמון בדיקת פג תוקף (Timeout) לאחר 5 דקות באמצעות Hangfire
            BackgroundJob.Schedule<MatchTimeoutService>(
                service => service.ProcessTimeoutsAndNoShowsAsync(DateTime.UtcNow),
                TimeSpan.FromMinutes(5)
            );
        }
    }
}