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
    public class DeliveryLogsController : ApiControllerBase
    {
        private readonly IDeliveryLogService _deliveryLogService;

        public DeliveryLogsController(IDeliveryLogService deliveryLogService)
        {
            _deliveryLogService = deliveryLogService;
        }

        // מנהל רואה הכל (בעימוד), מתנדב רק את הלוגים של המסלולים שלו
        [HttpGet]
        public async Task<ActionResult<List<DeliveryLogResponseDto>>> GetLogs([FromQuery] PageRequest page) =>
            Ok(await _deliveryLogService.GetLogsAsync(CurrentUser, page));

        // לוג חדש + עדכון מיקום חי של המתנדב (נשלח בזמן אמת לצופים במסלול)
        [HttpPost]
        public async Task<ActionResult<DeliveryLogResponseDto>> CreateLog([FromBody] CreateDeliveryLogDto logDto) =>
            Ok(await _deliveryLogService.CreateAsync(logDto, CurrentUser));
    }

    [Authorize]
    [Route("api/[controller]")]
    public class FeedbacksController : ApiControllerBase
    {
        private readonly IFeedbackService _feedbackService;

        public FeedbacksController(IFeedbackService feedbackService)
        {
            _feedbackService = feedbackService;
        }

        // מנהל רואה הכל, מתנדב את מה שנוגע אליו, עמותה/ספק את מה ששייך אליהם
        [HttpGet]
        public async Task<ActionResult<List<FeedbackResponseDto>>> GetFeedbacks([FromQuery] PageRequest page) =>
            Ok(await _feedbackService.GetFeedbacksAsync(CurrentUser, page));

        // משוב - רק העמותה של השידוך או הספק של התרומה, על שידוך שהושלם, פעם אחת לכל שידוך.
        // אחרי השמירה ציון האמינות של המתנדב מחושב מחדש.
        [HttpPost]
        public async Task<ActionResult<FeedbackResponseDto>> CreateFeedback([FromBody] CreateFeedbackDto feedbackDto) =>
            Ok(await _feedbackService.CreateAsync(feedbackDto, CurrentUser));
    }
}
