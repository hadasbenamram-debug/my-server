using FoodRescue.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using BLL.Interfaces;
using DAL.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    public class AuthControllerTests
    {
        [Fact]
        public async Task Register_RejectsShortPassword_ReturnsBadRequest()
        {
            var inMem = new Dictionary<string, string>
            {
                { "JwtSettings:Secret", "test-secret-which-is-long-enough" },
                { "JwtSettings:Issuer", "test-issuer" },
                { "JwtSettings:Audience", "test-audience" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMem).Build();

            var userRepo = new Mock<IUserRepository>();

            var controller = new AuthController(config, userRepo.Object);

            var model = new AuthController.RegisterModel
            {
                Name = "Name",
                Email = "short@example.com",
                Phone = "000",
                Password = "short"
            };
            // Sanity-check the test input
            Assert.Equal(5, model.Password.Length);

            var result = await controller.Register(model);
            // Diagnostic output
            try { System.Console.WriteLine($"Register result type: {result?.GetType().FullName}"); } catch { }

            Assert.IsType<BadRequestObjectResult>(result);
            var bad = result as BadRequestObjectResult;
            Assert.NotNull(bad);
            var msg = bad.Value?.ToString() ?? string.Empty;
            Assert.Contains("Password must be at least 8", msg);
        }

        [Fact]
        public async Task Register_ValidPassword_CreatesUser_ReturnsOkWithToken()
        {
            var inMem = new Dictionary<string, string>
            {
                { "JwtSettings:Secret", "test-secret-which-is-long-enough" },
                { "JwtSettings:Issuer", "test-issuer" },
                { "JwtSettings:Audience", "test-audience" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMem).Build();

            var userRepo = new Mock<IUserRepository>();
            userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User)null);
            userRepo.Setup(r => r.AddAsync(It.IsAny<User>())).Returns(Task.CompletedTask).Verifiable();

            var controller = new AuthController(config, userRepo.Object);

            var model = new AuthController.RegisterModel
            {
                Name = "Name",
                Email = "valid@example.com",
                Phone = "000",
                Password = "longenough"
            };

            var result = await controller.Register(model);

            Assert.IsType<OkObjectResult>(result);
            var ok = result as OkObjectResult;
            Assert.NotNull(ok);
            var tokenProp = ok.Value.GetType().GetProperty("Token");
            Assert.NotNull(tokenProp);
            var token = tokenProp.GetValue(ok.Value) as string;
            Assert.False(string.IsNullOrEmpty(token));

            userRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.Email == model.Email && !string.IsNullOrEmpty(u.PasswordHash))), Times.Once);
        }
    }
}
