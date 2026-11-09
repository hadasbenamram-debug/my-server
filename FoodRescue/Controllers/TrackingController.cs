using BLL.Exceptions;
using BLL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FoodRescue.Controllers
{
    // מעקב חי אחרי משלוח: העמותה המקבלת (וגם הספקים שבמסלול, המתנדב והמנהל) רואה את המסלול, את המתנדב
    // זז על המפה ואת ההתקדמות - הכול מיומן המשלוח (DeliveryLogs). אחרי הטעינה הלקוח מצטרף ב-SignalR
    // לקבוצת המסלול (WatchRoute) ומקבל כל רישום חדש ביומן בזמן אמת (DeliveryLogAdded).
    [Authorize]
    [Route("api/tracking")]
    public class TrackingController : ApiControllerBase
    {
        private readonly IDeliveryTrackingService _tracking;

        public TrackingController(IDeliveryTrackingService tracking)
        {
            _tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
        }

        // מסלול שלא קיים ומסלול שהמשתמש לא קשור אליו מקבלים אותה תשובה (404) - כדי לא לחשוף אילו מסלולים קיימים
        [HttpGet("routes/{routeId:int}")]
        public async Task<ActionResult<RouteTrackingDto>> GetRouteTracking(int routeId)
        {
            Response.Headers.CacheControl = "no-store, max-age=0";
            return Ok(await _tracking.GetRouteTrackingAsync(routeId, CurrentUser)
                      ?? throw new NotFoundException("המסלול לא נמצא."));
        }

        // המפה החיה של המנהל: כל המסלולים שבדרך עכשיו. אחרי הטעינה הלקוח מצטרף ב-SignalR ל-WatchAllRoutes
        // ומקבל כל רישום חדש ביומן של כל מסלול (מיקום, איסוף, מסירה).
        [HttpGet("live")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<RouteTrackingDto>>> GetLiveRoutes()
        {
            Response.Headers.CacheControl = "no-store, max-age=0";
            return Ok(await _tracking.GetLiveRoutesAsync(CurrentUser));
        }
    }
}
