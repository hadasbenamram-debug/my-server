using AutoMapper;
using BLL.Algorithms;
using BLL.Interfaces;
using BLL.Services;
using DAL.Classes;
using DTOs.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize] // דורש אימות ברירת מחדל לכל הבקר
    [Route("api/[controller]")]
    [ApiController]
    public class CommentsController : ControllerBase
    {
        private readonly CommentService _commentService;
        private readonly IRepository<Comment> _commentRepository;
        private readonly CommentTreeService _commentTreeService;
        private readonly IMapper _mapper;

        public CommentsController(
            IRepository<Comment> commentRepository,
            CommentTreeService commentTreeService,
            CommentService commentService,
            IMapper mapper)
        {
            _commentRepository = commentRepository;
            _commentTreeService = commentTreeService;
            _commentService = commentService;
            _mapper = mapper;
        }

        // שליפת עץ התגובות מעודכן לשימוש ב-BuildCommentTree
        [HttpGet("tree")]
        public async Task<ActionResult> GetCommentTree()
        {
            // 1. שליפת כל התגובות הגולמיות מהמסד
            var flatComments = await _commentRepository.GetAllAsync();

            // 2. מיפוי ל-CommentDto הנדרש על ידי אלגוריתם בניית העץ
            var commentDtos = flatComments.Select(c => new CommentDto
            {
                Id = c.Id,
                ParentId = c.ParentCommentId,
                Text = c.Content,
                AuthorName = c.Author
            });

            // 3. בניית העץ באמצעות השירות הנכון
            var tree = _commentTreeService.BuildCommentTree(commentDtos);

            return Ok(tree);
        }

        [HttpPost]
        public async Task<ActionResult> AddComment([FromBody] CommentDto1 dto)
        {
            if (dto == null) return BadRequest("נתונים שגויים.");

            // מיפוי מ-CommentDto1 למודל Comment של ה-DAL
            var commentEntity = _mapper.Map<Comment>(dto);

            await _commentService.AddCommentAsync(commentEntity);
            return Ok();
        }

        // שליפת כל התגובות - מנהלים בלבד
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult> GetComments()
        {
            var comments = await _commentService.GetAllCommentsAsync();
            return Ok(comments);
        }
    }
}