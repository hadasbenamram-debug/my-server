using BLL.Interfaces;
using DAL.Classes;
using FoodRescue.Controllers;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    public class AuthControllerTests
    {
        // הקונטרולר עובד רק מול IUserService (בלי Repositories, Cache או שירות מייל)
        private static AuthController CreateController(Mock<IUserService> userService) =>
            new AuthController(userService.Object);

        [Fact]
        public async Task Register_Success_ReturnsOkWithoutToken()
        {
            // ההרשמה רק יוצרת את המשתמש. את הטוקן מקבלים בהתחברות
            var service = new Mock<IUserService>();
            service.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
                   .ReturnsAsync(AuthResult.Success());

            var result = await CreateController(service).Register(new RegisterDto());

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.IsType<MessageResponse>(ok.Value);
        }

        [Fact]
        public async Task Register_EmailTaken_Returns409()
        {
            var service = new Mock<IUserService>();
            service.Setup(s => s.RegisterAsync(It.IsAny<RegisterDto>()))
                   .ReturnsAsync(AuthResult.Fail(AuthError.EmailTaken, "taken"));

            var result = await CreateController(service).Register(new RegisterDto());

            Assert.IsType<ConflictObjectResult>(result);
        }

        [Theory]
        [InlineData(AuthError.InvalidCredentials, 401)]
        [InlineData(AuthError.LockedOut, 423)]
        [InlineData(AuthError.NgoPendingApproval, 403)]
        [InlineData(AuthError.NgoRejected, 403)]
        [InlineData(AuthError.SupplierPendingApproval, 403)]
        [InlineData(AuthError.SupplierRejected, 403)]
        public async Task Login_Failure_MapsToExpectedStatusCode(AuthError error, int expectedStatus)
        {
            var service = new Mock<IUserService>();
            service.Setup(s => s.LoginAsync(It.IsAny<LoginDto>(), It.IsAny<string?>()))
                   .ReturnsAsync(AuthResult.Fail(error, "msg"));

            var result = await CreateController(service).Login(new LoginDto());

            var status = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(expectedStatus, status.StatusCode ?? (result is UnauthorizedObjectResult ? 401 : 0));
        }

        [Fact]
        public async Task Login_Success_ReturnsOk()
        {
            var service = new Mock<IUserService>();
            service.Setup(s => s.LoginAsync(It.IsAny<LoginDto>(), It.IsAny<string?>()))
                   .ReturnsAsync(AuthResult.Success(new AuthResponseDto { Token = "t", Role = "User" }));

            var result = await CreateController(service).Login(new LoginDto());

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task ForgotPassword_AlwaysReturnsOk_WithGenericMessage()
        {
            var service = new Mock<IUserService>();
            var result = await CreateController(service).ForgotPassword(new ForgotPasswordDto { Email = "x@y.com" });

            Assert.IsType<OkObjectResult>(result);
            service.Verify(s => s.ForgotPasswordAsync(It.IsAny<ForgotPasswordDto>()), Times.Once);
        }

        [Fact]
        public async Task ResetPassword_InvalidToken_ReturnsBadRequest()
        {
            var service = new Mock<IUserService>();
            service.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>())).ReturnsAsync(false);

            var result = await CreateController(service).ResetPassword(
                new ResetPasswordDto { Token = "bad", NewPassword = "longenough1" });

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}
