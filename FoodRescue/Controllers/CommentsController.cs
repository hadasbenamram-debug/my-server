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
    public class CommentsController : ApiControllerBase
    {
        private readonly ICommentService _commentService;

        public CommentsController(ICommentService commentService)
        {
            _commentService = commentService;
        }

        // עץ התגובות (רקורסיבי) - מנהל רואה הכל, כל משתמש אחר רק תגובות על תרומות/שידוכים שהוא קשור אליהם
        [HttpGet("tree")]
        public async Task<ActionResult<List<CommentDto>>> GetCommentTree() =>
            Ok(await _commentService.GetTreeAsync(CurrentUser));

        // תגובה חדשה חייבת להיות על תרומה/שידוך (DonationId / MatchId), או תשובה לתגובה קיימת (ParentCommentId)
        [HttpPost]
        public async Task<ActionResult<CommentDto>> AddComment([FromBody] CreateCommentDto dto) =>
            Ok(await _commentService.AddAsync(dto, CurrentUser));

        // ניהול תגובות - מנהלים בלבד
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<CommentDto>>> GetComments() =>
            Ok(await _commentService.GetAllAsync());

        // מחיקת תגובה כולל כל התשובות שמתחתיה
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteComment(int id)
        {
            var deleted = await _commentService.DeleteAsync(id);
            return Ok(new { message = "התגובה נמחקה.", deleted });
        }
    }
}
