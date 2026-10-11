using AutoMapper;
using AutoMapper.QueryableExtensions;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using FoodRescue.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DeliveryLogsController : ControllerBase
    {
        private readonly FoodRescueDbContext _context;
        private readonly IMapper _mapper;
        private readonly CallerProfileService _caller;

        public DeliveryLogsController(FoodRescueDbContext context, IMapper mapper, CallerProfileService caller)
        {
            _context = context;
            _mapper = mapper;
            _caller = caller;
        }

        // צפייה בלוגים - מנהל רואה הכל, מתנדב רואה רק לוגים של המסלולים שלו
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DeliveryLogResponseDto>>> GetLogs()
        {
            var caller = await _caller.GetAsync();

            // אין צורך ב-Include! ה-ProjectTo ידאג לחיבור הנדרש אוטומטית.
            IQueryable<DeliveryLog> query = _context.DeliveryLogs;

            if (!caller.IsAdmin)
            {
                if (caller.Volunteer == null) return Forbid();
                var volunteerId = caller.Volunteer.Id;
                query = query.Where(l => l.VolunteerRoute.VolunteerId == volunteerId);
            }

            // המרה ישירה ל-DTO באמצעות ProjectTo ושליפה במסד הנתונים
            var logDtos = await query
                .ProjectTo<DeliveryLogResponseDto>(_mapper.ConfigurationProvider)
                .ToListAsync();

            return Ok(logDtos);
        }

        // יצירת לוג ושמירתו + עדכון מיקום חי בטבלת המתנדבים בפעולה אחת
        [HttpPost]
        public async Task<ActionResult<DeliveryLogResponseDto>> CreateLog([FromBody] CreateDeliveryLogDto logDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var route = await _context.VolunteerRoutes.FirstOrDefaultAsync(r => r.Id == logDto.VolunteerRouteId);
            if (route == null) return BadRequest(new { message = "מסלול המתנדב לא נמצא." });

            var caller = await _caller.GetAsync();
            if (!caller.IsAdmin && (caller.Volunteer == null || route.VolunteerId != caller.Volunteer.Id))
                return Forbid();

            // 1. הוספת הלוג לטבלה
            var logEntity = _mapper.Map<DeliveryLog>(logDto);
            logEntity.Id = 0;
            logEntity.Timestamp = DateTime.UtcNow;
            logEntity.Latitude = logDto.Latitude ?? 0;
            logEntity.Longitude = logDto.Longitude ?? 0;

            await _context.DeliveryLogs.AddAsync(logEntity);

            // 2. עדכון המיקום הנוכחי של המתנדב בטבלת Volunteers
            var volunteer = await _context.Volunteers.FirstOrDefaultAsync(v => v.Id == route.VolunteerId);
            if (volunteer != null)
            {
                volunteer.CurrentLatitude = logDto.Latitude ?? 0;
                volunteer.CurrentLongitude = logDto.Longitude ?? 0;
            }

            // 3. שמירת השינויים במסד הנתונים
            await _context.SaveChangesAsync();

            var resultDto = _mapper.Map<DeliveryLogResponseDto>(logEntity);
            return Ok(resultDto);
        }
    }

    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class FeedbacksController : ControllerBase
    {
        private readonly FoodRescueDbContext _context;
        private readonly IMapper _mapper;
        private readonly CallerProfileService _caller;

        public FeedbacksController(FoodRescueDbContext context, IMapper mapper, CallerProfileService caller)
        {
            _context = context;
            _mapper = mapper;
            _caller = caller;
        }

        // צפייה במשובים: מנהל רואה הכל, מתנדב רואה את מה שנוגע אליו, עמותה/ספק רואה את מה ששייך אליה
        [HttpGet]
        public async Task<ActionResult<IEnumerable<FeedbackResponseDto>>> GetFeedbacks()
        {
            var caller = await _caller.GetAsync();

            // אין צורך ב-Include וב-ThenInclude! ה-ProjectTo מטפל בזה אוטומטית.
            var query = _context.Feedbacks.AsQueryable();

            // 1. מנהל רואה את כל המשובים במערכת
            if (caller.IsAdmin)
            {
                var feedbackDtos = await query
                    .ProjectTo<FeedbackResponseDto>(_mapper.ConfigurationProvider)
                    .ToListAsync();
                return Ok(feedbackDtos);
            }

            // 2. מתנדב רואה משובים הקשורים למסלולים שלו
            if (caller.Volunteer != null)
            {
                var volunteerId = caller.Volunteer.Id;
                var volunteerFeedbackDtos = await query
                    .Where(f => f.Match != null && f.Match.VolunteerRoute != null && f.Match.VolunteerRoute.VolunteerId == volunteerId)
                    .ProjectTo<FeedbackResponseDto>(_mapper.ConfigurationProvider)
                    .ToListAsync();
                return Ok(volunteerFeedbackDtos);
            }

            // 3. עמותה / ספק רואים רק את המשובים הקשורים לשידוכים או לתרומות שלהם
            // (שליפת הישויות במלואן לצורך הפעלת הלוגיקה המותאמת של caller)
            var allFeedbacks = await query.ToListAsync();
            var filteredFeedbacks = allFeedbacks
                .Where(f => f.Match != null && (caller.IsMatchNgo(f.Match) || caller.OwnsDonation(f.Match.Donation)))
                .ToList();

            return Ok(_mapper.Map<IEnumerable<FeedbackResponseDto>>(filteredFeedbacks));
        }

        // יצירת משוב - רק העמותה של השידוך או הספק של התרומה, על שידוך שהושלם, ופעם אחת לכל שידוך.
        [HttpPost]
        public async Task<ActionResult<FeedbackResponseDto>> CreateFeedback([FromBody] CreateFeedbackDto feedbackDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var match = await _context.Matches
                .Include(m => m.Donation)
                .FirstOrDefaultAsync(m => m.Id == feedbackDto.MatchId);
            if (match == null) return BadRequest(new { message = "השידוך לא נמצא." });

            var caller = await _caller.GetAsync();
            if (!caller.IsMatchNgo(match) && !caller.OwnsDonation(match.Donation))
                return Forbid();

            if (match.Status != MatchStatus.Completed)
                return BadRequest(new { message = "ניתן להשאיר משוב רק על שידוך שהושלם." });

            if (await _context.Feedbacks.AnyAsync(f => f.MatchId == match.Id))
                return Conflict(new { message = "כבר קיים משוב עבור שידוך זה." });

            var feedbackEntity = _mapper.Map<Feedback>(feedbackDto);

            // אבטחה: איפוס מזהה וקביעת תאריך שרת
            feedbackEntity.Id = 0;
            feedbackEntity.CreatedAt = DateTime.UtcNow;

            _context.Feedbacks.Add(feedbackEntity);
            await _context.SaveChangesAsync();

            var resultDto = _mapper.Map<FeedbackResponseDto>(feedbackEntity);
            return Ok(resultDto);
        }
    }
}