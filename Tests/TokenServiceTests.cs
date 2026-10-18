using BLL.Interfaces;
using BLL.Services;
using DAL.Classes;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Xunit;

namespace FoodRescue.Tests
{
    public class TokenServiceTests
    {
        private const string Secret = "0123456789abcdef0123456789abcdef-test";

        private sealed class FixedClock : TimeProvider
        {
            private readonly DateTimeOffset _now;
            public FixedClock(DateTimeOffset now) => _now = now;
            public override DateTimeOffset GetUtcNow() => _now;
        }

        private static IConfiguration Config(Dictionary<string, string?>? extra = null)
        {
            var data = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = Secret,
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:ExpiresMinutes"] = "30"
            };

            if (extra != null)
                foreach (var kv in extra) data[kv.Key] = kv.Value;

            return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        }

        private static readonly User TestUser =
            new() { Id = 7, Email = "u@example.com", Name = "U", Role = UserRole.User };

        private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

        [Fact]
        public void CreateToken_ContainsIatAndConfiguredExpiry()
        {
            var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
            ITokenService service = new TokenService(Config(), new FixedClock(now));

            var result = service.CreateToken(TestUser);
            var jwt = Read(result.Token);

            Assert.Equal(now.AddMinutes(30).UtcDateTime, result.ExpiresAt);
            Assert.Equal(now.AddMinutes(30).UtcDateTime, jwt.ValidTo);

            var iat = jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Iat);
            Assert.Equal(now.ToUnixTimeSeconds().ToString(), iat.Value);

            Assert.Equal("test-issuer", jwt.Issuer);
            Assert.Contains("test-audience", jwt.Audiences);
        }

        [Fact]
        public void CreateToken_DefaultLifetime_IsNotThreeHours()
        {
            var cfg = Config(new() { ["Jwt:ExpiresMinutes"] = null });
            var now = DateTimeOffset.UtcNow;
            var result = new TokenService(cfg, new FixedClock(now)).CreateToken(TestUser);

            Assert.Equal(now.AddMinutes(60).UtcDateTime, result.ExpiresAt);
        }

        // קישורי המייל כבר לא מכילים JWT. הטוקן היחיד שהשירות מנפיק הוא טוקן ההתחברות.
        [Fact]
        public void TokenService_NoLongerIssuesEmailOrMatchLinkJwts()
        {
            var methods = typeof(ITokenService).GetMethods().Select(m => m.Name).ToList();
            Assert.Equal(new[] { nameof(ITokenService.CreateToken) }, methods);
        }

        // החותמת בטוקן מאפשרת לבטל את כל הטוקנים הישנים אחרי שינוי / איפוס סיסמה
        [Fact]
        public void CreateToken_CarriesTheUsersSecurityStamp()
        {
            var user = new User { Id = 7, Email = "u@example.com", Name = "U", Role = UserRole.Volunteer, SecurityStamp = "stamp-123" };
            var jwt = Read(new TokenService(Config()).CreateToken(user).Token);

            Assert.Equal("stamp-123", jwt.Claims.Single(c => c.Type == TokenService.SecurityStampClaimType).Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("too-short")]
        public void Constructor_RejectsMissingOrShortSecret(string? secret)
        {
            var cfg = Config(new() { ["Jwt:Key"] = secret });
            Assert.Throws<InvalidOperationException>(() => new TokenService(cfg));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("abc")]
        public void Constructor_RejectsInvalidExpiry(string minutes)
        {
            var cfg = Config(new() { ["Jwt:ExpiresMinutes"] = minutes });
            Assert.Throws<InvalidOperationException>(() => new TokenService(cfg));
        }
    }
}
