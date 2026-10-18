using FoodRescue.DTOs;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Xunit;

namespace FoodRescue.Tests
{
    public class RegisterDtoValidationTests
    {
        private static List<ValidationResult> Validate(object model)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
            return results;
        }

        private static RegisterDto Valid() => new()
        {
            Name = "Dana Cohen",
            Email = "dana@example.com",
            Phone = "0501234567",
            Password = "longenough1"
        };

        [Fact]
        public void ValidDto_HasNoErrors() => Assert.Empty(Validate(Valid()));

        [Fact]
        public void ShortPassword_IsRejected()
        {
            var dto = Valid();
            dto.Password = "short";
            Assert.Contains(Validate(dto), r => r.MemberNames.Contains(nameof(RegisterDto.Password)));
        }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("a@")]
        [InlineData("")]
        public void InvalidEmail_IsRejected(string email)
        {
            var dto = Valid();
            dto.Email = email;
            Assert.Contains(Validate(dto), r => r.MemberNames.Contains(nameof(RegisterDto.Email)));
        }

        [Fact]
        public void RegisterDto_DoesNotExposeRoleOrPrivilegedFields()
        {
            var names = typeof(RegisterDto).GetProperties().Select(p => p.Name).ToList();

            Assert.DoesNotContain("Role", names);
            Assert.DoesNotContain("IsEmailVerified", names);
            Assert.DoesNotContain("Id", names);
        }

        [Fact]
        public void LoginDto_DoesNotEnforcePasswordPolicy()
        {
            var dto = new LoginDto { Email = "x@y.com", Password = "a" };
            Assert.Empty(Validate(dto));
        }
    }
}
