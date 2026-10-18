using AutoMapper;
using BLL.Exceptions;
using BLL.Interfaces;
using BLL.Notifications;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class DeliveryLogService : IDeliveryLogService
    {
        private readonly IDeliveryLogRepository _deliveryLogRepository;
        private readonly IVolunteerRouteRepository _routeRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly INotificationService _notifications;
        private readonly CallerProfileService _caller;
        private readonly IMapper _mapper;

        public DeliveryLogService(
            IDeliveryLogRepository deliveryLogRepository,
            IVolunteerRouteRepository routeRepository,
            IVolunteerRepository volunteerRepository,
            INotificationService notifications,
            CallerProfileService caller,
            IMapper mapper)
        {
            _deliveryLogRepository = deliveryLogRepository;
            _routeRepository = routeRepository;
            _volunteerRepository = volunteerRepository;
            _notifications = notifications;
            _caller = caller;
            _mapper = mapper;
        }

        public async Task<List<DeliveryLogResponseDto>> GetLogsAsync(CurrentUserDto currentUser, PageRequest page)
        {
            var caller = await _caller.GetAsync(currentUser);

            if (caller.IsAdmin)
                return _mapper.Map<List<DeliveryLogResponseDto>>((await _deliveryLogRepository.GetPageAsync(page.Skip, page.PageSize)).Items);

            if (caller.Volunteer == null)
                throw new ForbiddenException("רק מנהל או מתנדב יכולים לצפות ביומני המשלוחים.");

            return _mapper.Map<List<DeliveryLogResponseDto>>(await _deliveryLogRepository.GetByVolunteerIdAsync(caller.Volunteer.Id));
        }

        public async Task<DeliveryLogResponseDto> CreateAsync(CreateDeliveryLogDto dto, CurrentUserDto currentUser)
        {
            // מסלול שלא קיים ומסלול של מתנדב אחר מקבלים אותה תשובה (404), כדי לא לחשוף אילו מזהים קיימים.
            const string routeNotFound = "מסלול המתנדב לא נמצא.";

            var route = await _routeRepository.GetByIdAsync(dto.VolunteerRouteId)
                        ?? throw new NotFoundException(routeNotFound);

            var caller = await _caller.GetAsync(currentUser);
            if (!caller.IsAdmin && (caller.Volunteer == null || route.VolunteerId != caller.Volunteer.Id))
                throw new NotFoundException(routeNotFound);

            var logEntity = _mapper.Map<DeliveryLog>(dto);
            logEntity.Id = 0;
            logEntity.Timestamp = DateTime.UtcNow;
            await _deliveryLogRepository.AddAsync(logEntity);

            // עדכון המיקום הנוכחי של המתנדב (רק כשנשלחו קואורדינטות) ושידור למי שעוקב אחרי המסלול
            if (dto.Latitude.HasValue && dto.Longitude.HasValue)
            {
                var volunteer = await _volunteerRepository.GetByIdAsync(route.VolunteerId);
                if (volunteer != null)
                {
                    volunteer.CurrentLatitude = dto.Latitude.Value;
                    volunteer.CurrentLongitude = dto.Longitude.Value;
                    volunteer.LocationUpdatedAt = logEntity.Timestamp;
                    await _volunteerRepository.UpdateAsync(volunteer);
                }

                await _notifications.PublishAsync(RealtimeGroups.Route(route.Id), RealtimeEvents.VolunteerLocation, new
                {
                    routeId = route.Id,
                    volunteerId = route.VolunteerId,
                    latitude = dto.Latitude.Value,
                    longitude = dto.Longitude.Value,
                    at = logEntity.Timestamp
                });
            }

            return _mapper.Map<DeliveryLogResponseDto>(logEntity);
        }
    }
}
