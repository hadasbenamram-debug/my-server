using BLL.Algorithms;
using BLL.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodRescue.Hubs
{
    [Authorize]
    public class LocationHub : Hub
    {
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly DistanceMemoizationService _distanceCache;

        public LocationHub(IVolunteerRepository volunteerRepository, DistanceMemoizationService distanceCache)
        {
            _volunteerRepository = volunteerRepository;
            _distanceCache = distanceCache;
        }

        private const string AdminsGroup = "Admins";

        // מנהלים מצטרפים לקבוצה שמקבלת עדכוני מיקום (במקום שידור לכל המחוברים)
        public override async Task OnConnectedAsync()
        {
            if (Context.User?.IsInRole("Admin") == true)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
            }
            await base.OnConnectedAsync();
        }

        // מתנדב מעדכן מיקום - המזהה נלקח ישירות מתוך ה-JWT המאומת (Context.User) ולא מהקליינט
        public async Task SendLocation(double latitude, double longitude)
        {
            // שליפת מזהה המשתמש מתוך הטוקן (תומך ב-NameIdentifier או sub)
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? Context.User?.FindFirst("sub")?.Value;

            if (!int.TryParse(userIdClaim, out int userId)) return;

            // ה-claim הוא UserId, ולכן מחפשים את פרופיל המתנדב לפי UserId (ולא לפי Volunteer.Id)
            var volunteer = await _volunteerRepository.GetByUserIdAsync(userId);
            if (volunteer == null || volunteer.ApprovalStatus != DAL.Classes.ApprovalStatus.Approved) return;

            volunteer.CurrentLatitude = latitude;
            volunteer.CurrentLongitude = longitude;
            volunteer.LocationUpdatedAt = DateTime.UtcNow;
            await _volunteerRepository.UpdateAsync(volunteer);

            // ביטול מרחקים ממוזכרים של המתנדב הזה - הם חושבו ביחס למיקום הישן
            _distanceCache.InvalidateForVolunteer(volunteer.Id);

            // מעביר את המיקום המעודכן למנהלים בלבד (מסך הניהול)
            await Clients.Group(AdminsGroup).SendAsync("ReceiveLocation", volunteer.Id.ToString(), latitude, longitude);
        }
    }
}