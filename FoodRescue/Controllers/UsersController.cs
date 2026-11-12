using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class UsersController : ApiControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        // כל המשתמשים - מנהלים בלבד, בעימוד (?page=1&pageSize=100; הסך בכותרת X-Total-Count)
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<UserDto>>> GetUsers([FromQuery] PageRequest page) =>
            Paged(await _userService.GetPageAsync(page));

        [HttpGet("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<UserDto>> GetUser(int id) =>
            Ok(await _userService.GetByIdAsync(id) ?? throw new NotFoundException("המשתמש לא נמצא."));

        // תיקון 51: PUT api/users/{id}/verification - מנהל מסמן ידנית שהמייל / הטלפון אומתו (או מבטל אימות)
        [HttpPut("{id:int}/verification")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<UserDto>> SetVerification(int id, [FromBody] UpdateVerificationDto dto) =>
            Ok(await _userService.SetVerificationAsync(id, dto) ?? throw new NotFoundException("המשתמש לא נמצא."));

        // GET api/users/me - הפרופיל של המשתמש המחובר (כל תפקיד)
        [HttpGet("me")]
        public async Task<ActionResult<MyProfileDto>> GetMe() =>
            Ok(await _userService.GetMyProfileAsync(RequireUserId()) ?? throw new UnauthorizedException("המשתמש לא נמצא."));

        // PUT api/users/me - עדכון שם וטלפון (שינוי טלפון מבטל את אימות הטלפון)
        [HttpPut("me")]
        public async Task<ActionResult<MyProfileDto>> UpdateMe([FromBody] UpdateMyProfileDto dto) =>
            Ok(await _userService.UpdateMyProfileAsync(RequireUserId(), dto) ?? throw new UnauthorizedException("המשתמש לא נמצא."));
    }
}
