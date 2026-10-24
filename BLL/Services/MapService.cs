using AutoMapper;
using BLL.Integrations.GoogleMaps;
using BLL.Interfaces;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    // כל הישויות (ספקים, עמותות ומתנדבים) לתמונת המצב המרחבית, ותמונות המפה עצמן.
    // התמונות מגיעות מ-Google דרך השרת (IStaticMapService), כך שהמפתח של Google לא מגיע ללקוח.
    public class MapService : IMapService
    {
        // אותם צבעים כמו במקרא שבלקוח (MAP_COLORS ב-GoogleMapView.tsx)
        private const string SupplierColor = "e08a1e";
        private const string NgoColor = "159aa0";
        private const string VolunteerColor = "33383a";
        private const string OtherColor = "8a8f91";

        private readonly ISupplierRepository _supplierRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IMatchService _matchService;
        private readonly IStaticMapService _staticMap;
        private readonly IMapper _mapper;

        public MapService(
            ISupplierRepository supplierRepository,
            INgoRepository ngoRepository,
            IVolunteerRepository volunteerRepository,
            IMatchService matchService,
            IStaticMapService staticMap,
            IMapper mapper)
        {
            _supplierRepository = supplierRepository;
            _ngoRepository = ngoRepository;
            _volunteerRepository = volunteerRepository;
            _matchService = matchService;
            _staticMap = staticMap;
            _mapper = mapper;
        }

        public async Task<List<MapEntityDto>> GetAllEntitiesAsync()
        {
            var entities = new List<MapEntityDto>();

            var suppliers = await _supplierRepository.GetAllAsync();
            var ngos = await _ngoRepository.GetAllAsync();
            var volunteers = await _volunteerRepository.GetAllWithUserAsync();

            entities.AddRange(_mapper.Map<List<MapEntityDto>>(suppliers));
            entities.AddRange(_mapper.Map<List<MapEntityDto>>(ngos));
            entities.AddRange(_mapper.Map<List<MapEntityDto>>(volunteers));

            return entities;
        }

        public async Task<MapImageDto> GetAllEntitiesImageAsync(IReadOnlyCollection<string> hiddenTypes)
        {
            var entities = await GetAllEntitiesAsync();

            var pins = entities
                .Where(e => !hiddenTypes.Contains(e.Type, StringComparer.OrdinalIgnoreCase))
                .Select(e => new MapPin(e.Latitude, e.Longitude, ColorFor(e.Type)))
                .ToList();

            return await _staticMap.RenderAsync(pins);
        }

        public async Task<MapImageDto?> GetRouteImageAsync(int routeId, CurrentUserDto currentUser)
        {
            // בדיקת ההרשאה נעשית ב-MatchService: null = המסלול לא קיים, או שהמשתמש לא קשור אליו
            var route = await _matchService.GetRouteAsync(routeId, currentUser);
            if (route == null)
                return null;

            // התחנות לפי סדר הנסיעה (1, 2, 3...) והעמותה אחרונה - אותו מספור כמו ברשימה שבמסך
            var pins = route.Stops
                .Select((stop, index) => new MapPin(stop.Latitude, stop.Longitude, SupplierColor, StopLabel(index)))
                .ToList();
            pins.Add(new MapPin(route.NgoLatitude, route.NgoLongitude, NgoColor, StopLabel(route.Stops.Count)));

            // קו המסלול בכבישים (מ-Google Routes, נשמר במסלול). אם אין - קו ישר בין התחנות.
            return await _staticMap.RenderAsync(pins, route.Polyline, connectPins: true);
        }

        private static string ColorFor(string type) => type switch
        {
            "supplier" => SupplierColor,
            "ngo" => NgoColor,
            "volunteer" => VolunteerColor,
            _ => OtherColor
        };

        // ב-Google התווית היא תו אחד: 1-9 ואחר כך A-Z
        private static char StopLabel(int index) =>
            index < 9 ? (char)('1' + index) : (char)('A' + Math.Min(index - 9, 25));
    }
}
