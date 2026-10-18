using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // הקונטרולר רק מקבל את הבקשה, מעביר אותה ל-IDonationService ומחזיר תשובה.
    // אין כאן לוגיקה עסקית, Entities, גישה למסד או try/catch (השגיאות מטופלות במרכז).
    [Authorize]
    [Route("api/[controller]")]
    public class DonationsController : ApiControllerBase
    {
        private const string DonationNotFound = "התרומה לא נמצאה.";

        private readonly IDonationService _donationService;

        public DonationsController(IDonationService donationService)
        {
            _donationService = donationService;
        }

        // ניתוח תמונת תרומה (AI). התמונה נרשמת כשייכת למשתמש, ורק הוא יכול לצרף אותה לתרומה.
        [HttpPost("analyze-image")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(6 * 1024 * 1024)]
        [EnableRateLimiting("image-analysis")]
        public async Task<ActionResult<ImageAnalysisResponseDto>> AnalyzeDonationImage([FromForm] AnalyzeImageRequest imageRequest) =>
            Ok(await _donationService.AnalyzeImageAsync(RequireUserId(), imageRequest?.Image!, HttpContext.RequestAborted));

        // תרומות של הספק המחובר
        [HttpGet("my-supplier-donations")]
        public async Task<ActionResult<List<SupplierDonationSummaryDto>>> GetMySupplierDonations() =>
            Ok(await _donationService.GetMySupplierDonationsAsync(RequireUserId()));

        // תרומות שהוקצו לעמותה המחוברת (כולל המתנדב, זמן ההגעה והסטטוס)
        [HttpGet("my-ngo-donations")]
        public async Task<ActionResult<IEnumerable<DonationResponseDto>>> GetMyNgoDonations()
        {
            RequireUserId();
            return Ok(await _donationService.GetMyNgoDonationsAsync(CurrentUser));
        }

        // כל התרומות - מנהל בלבד, בעימוד
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<AdminDonationSummaryDto>>> GetDonations([FromQuery] PageRequest page) =>
            Paged(await _donationService.GetAllForAdminAsync(page));

        [HttpGet("{id:int}")]
        public async Task<ActionResult<DonationResponseDto>> GetDonation(int id) =>
            // אותה תשובה גם כשהתרומה לא קיימת וגם כשאין הרשאה - כדי לא לחשוף שהיא קיימת.
            Ok(await _donationService.GetByIdAsync(id, CurrentUser) ?? throw new NotFoundException(DonationNotFound));

        // תרומות פתוחות לשיבוץ (מנהל / מתנדב מאושר)
        [HttpGet("available")]
        public async Task<ActionResult<IEnumerable<DonationResponseDto>>> GetAvailableDonations() =>
            Ok(await _donationService.GetAvailableAsync(CurrentUser));

        [HttpPost]
        public async Task<ActionResult<DonationResponseDto>> CreateDonation([FromBody] CreateDonationDto donationDto)
        {
            var created = await _donationService.CreateAsync(donationDto, RequireUserId());
            return CreatedAtAction(nameof(GetDonation), new { id = created.Id }, created);
        }

        // חדש: הספק עורך תרומה שלו (כל עוד היא ממתינה לשיבוץ)
        [HttpPut("{id:int}")]
        public async Task<ActionResult<DonationResponseDto>> UpdateDonation(int id, [FromBody] UpdateDonationDto dto) =>
            Ok(await _donationService.UpdateAsync(id, dto, CurrentUser));

        // ביטול תרומה שעוד לא נאספה - הספק עצמו (חדש) או מנהל. שידוך פעיל מבוטל והמתנדב מקבל הודעה.
        [HttpPut("{id:int}/cancel")]
        public async Task<IActionResult> CancelDonation(int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelDonationDto? dto)
        {
            await _donationService.CancelAsync(id, CurrentUser, dto?.Reason);
            return Message("התרומה בוטלה.");
        }

        // חדש: העמותה מאשרת שקיבלה את התרומה (Delivered -> Completed). מחזיר את מזהה השידוך לדירוג.
        [HttpPost("{id:int}/confirm-receipt")]
        public async Task<IActionResult> ConfirmReceipt(int id)
        {
            var matchId = await _donationService.ConfirmReceiptAsync(id, CurrentUser);
            return Ok(new { message = "קבלת התרומה אושרה. תודה!", matchId });
        }
    }
}
