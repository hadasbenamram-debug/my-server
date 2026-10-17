using BLL.Algorithms;
using DAL.Classes;
using DAL.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace BLL.Services
{
    // יצירת מנהל ראשי בעליית השרת, אם הוגדרו פרטי התחברות ב-AdminSeed.
    // (הקוד עבר לכאן מ-Program.cs, כדי ש-Program.cs לא ייגש ישירות ל-DbContext.)
    public class AdminSeedService
    {
        private readonly IUserRepository _userRepository;
        private readonly IManagerRepository _managerRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AdminSeedService> _logger;

        public AdminSeedService(
            IUserRepository userRepository,
            IManagerRepository managerRepository,
            IConfiguration configuration,
            ILogger<AdminSeedService> logger)
        {
            _userRepository = userRepository;
            _managerRepository = managerRepository;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SeedAdminAsync()
        {
            string? adminEmail =
                string.IsNullOrWhiteSpace(_configuration["AdminSeed:Email"])
                    ? null
                    : EmailNormalizer.Normalize(_configuration["AdminSeed:Email"]);

            string? adminPassword = _configuration["AdminSeed:Password"];
            string adminName = _configuration["AdminSeed:Name"] ?? "מנהל ראשי";
            string adminPhone = _configuration["AdminSeed:Phone"] ?? "0500000000";
            string adminDepartment = _configuration["AdminSeed:Department"] ?? "הנהלה ראשית";

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                _logger.LogWarning("Admin seeding skipped because AdminSeed:Email or AdminSeed:Password is missing.");
                return;
            }

            var existingUser = await _userRepository.GetByEmailAsync(adminEmail);

            if (existingUser == null)
            {
                existingUser = new User
                {
                    Name = adminName,
                    Email = adminEmail,
                    Phone = adminPhone,
                    PasswordHash = PasswordHasher.Hash(adminPassword),
                    Role = UserRole.Admin,
                    IsEmailVerified = true,
                    IsPhoneVerified = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _userRepository.AddAsync(existingUser);
            }

            var existingManager = await _managerRepository.GetByUserIdAsync(existingUser.Id);

            if (existingManager == null)
            {
                await _managerRepository.AddAsync(new Manager
                {
                    UserId = existingUser.Id,
                    Department = adminDepartment
                });
            }
        }
    }
}
