using AutoMapper;
using BLL.Interfaces;
using DAL.Classes;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace FoodRescue.Controllers
{
    [Authorize] // דורש אימות כללי לכל ה־Endpoints בבקר זה כברירת מחדל
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IMapper _mapper;

        public UsersController(IUserRepository userRepository, IMapper mapper)
        {
            _userRepository = userRepository;
            _mapper = mapper;
        }

        // שליפת כל המשתמשים - מוגבל למנהלים בלבד (סעיף 11)
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
        {
            var users = await _userRepository.GetAllAsync();
            var userDtos = _mapper.Map<IEnumerable<UserDto>>(users);
            return Ok(userDtos);
        }

        // שליפת משתמש לפי מזהה - מוגבל למנהלים בלבד
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<UserDto>> GetUser(int id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) return NotFound();

            var userDto = _mapper.Map<UserDto>(user);
            return Ok(userDto);
        }

        // הרשמת משתמשים מתבצעת אך ורק דרך POST /api/Auth/register.
        // ה-endpoint הישן POST /api/Users/register הוסר (היה לא עקבי: בלי Phone, בלי בדיקת סיסמה/מייל כפול ובלי JWT).
    }
}