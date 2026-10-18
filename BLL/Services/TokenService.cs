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
            var section = configuration.GetSection("JwtSettings");
            var secret = section["Secret"];

            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException(
                    "JwtSettings:Secret is not configured. Set it via user-secrets or environment variables.");

            if (Encoding.UTF8.GetByteCount(secret) < 32)
                throw new InvalidOperationException(
                    "JwtSettings:Secret must be at least 32 bytes long (use a random 256-bit value).");

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

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Sub, user.Email),
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
                    $"JwtSettings:{key} must be a positive whole number of minutes.");
            }

            return TimeSpan.FromMinutes(minutes);
        }
    }
}
