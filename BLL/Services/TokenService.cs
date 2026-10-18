using BLL.Interfaces;
using DAL.Classes;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BLL.Services
{
    public sealed class TokenService : ITokenService
    {
        // חותמת האבטחה של המשתמש בזמן ההנפקה. אחרי שינוי סיסמה החותמת במסד מתחלפת והטוקן נדחה.
        public const string SecurityStampClaimType = "sstamp";

        private const int DefaultExpiresMinutes = 60;

        private readonly SigningCredentials _credentials;
        private readonly string? _issuer;
        private readonly string? _audience;
        private readonly TimeSpan _loginLifetime;
        private readonly TimeProvider _clock;

        public TokenService(IConfiguration configuration, TimeProvider? clock = null)
        {
            var section = configuration.GetSection("Jwt");
            var secret = section["Key"];

            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException(
                    "Jwt:Key is missing. Set the environment variable Jwt__Key, or in development: dotnet user-secrets set \"Jwt:Key\" \"<random value of at least 32 characters>\".");

            if (Encoding.UTF8.GetByteCount(secret) < 32)
                throw new InvalidOperationException(
                    "Jwt:Key must be at least 32 bytes long (HS256 needs a 256-bit key). Use a long random value.");

            _credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256);

            _issuer = section["Issuer"];
            _audience = section["Audience"];
            _loginLifetime = ReadMinutes(section, "ExpiresMinutes", DefaultExpiresMinutes);
            _clock = clock ?? TimeProvider.System;
        }

        public TokenResult CreateToken(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            DateTimeOffset now = _clock.GetUtcNow();
            DateTime expires = now.Add(_loginLifetime).UtcDateTime;

            // ה-claims: מזהה, מייל ותפקיד - בלי מידע רגיש (הטוקן מקודד, לא מוצפן).
            // בנוסף: חותמת האבטחה (לביטול טוקנים אחרי החלפת סיסמה), מזהה טוקן ייחודי (jti) וזמן ההנפקה (iat).
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(SecurityStampClaimType, user.SecurityStamp ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64)
            };

            var jwt = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: expires,
                signingCredentials: _credentials);

            return new TokenResult(new JwtSecurityTokenHandler().WriteToken(jwt), expires);
        }

        private static TimeSpan ReadMinutes(IConfigurationSection section, string key, int fallback)
        {
            var raw = section[key];

            if (string.IsNullOrWhiteSpace(raw))
                return TimeSpan.FromMinutes(fallback);

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) ||
                minutes <= 0)
            {
                throw new InvalidOperationException(
                    $"Jwt:{key} must be a positive whole number of minutes.");
            }

            return TimeSpan.FromMinutes(minutes);
        }
    }
}
