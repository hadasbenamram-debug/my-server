using DAL.Classes;
using DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    // יצירת אזורי פעילות בסיסיים בעליית השרת, אם הטבלה ריקה.
    // (באותו דפוס כמו AdminSeedService ו-FoodCategorySeedService.) אם כבר יש אזורים - לא נוגעים בהם.
    // בלי אזורים אי אפשר ליצור תרומה (FK_Donations_Regions_RegionId), להירשם כעמותה או להגדיר זמינות מתנדב.
    public class RegionSeedService
    {
        // מרכז האזור משמש כנקודת מוצא למתנדב שאין לו מיקום GPS. אפשר לשנות/להוסיף אזורים דרך POST api/Lookups/regions.
        private static readonly (string Name, double Lat, double Lng, double RadiusKm)[] DefaultRegions =
        {
            ("ירושלים והסביבה", 31.7683, 35.2137, 20),
            ("תל אביב והמרכז", 32.0853, 34.7818, 20),
            ("השרון", 32.3215, 34.8532, 20),
            ("השפלה", 31.8928, 34.8113, 20),
            ("חיפה והצפון", 32.7940, 34.9896, 30),
            ("באר שבע והדרום", 31.2520, 34.7915, 35),
        };

        private readonly IRegionRepository _regionRepository;
        private readonly ILogger<RegionSeedService> _logger;

        public RegionSeedService(
            IRegionRepository regionRepository,
            ILogger<RegionSeedService> logger)
        {
            _regionRepository = regionRepository;
            _logger = logger;
        }

        public async Task SeedRegionsAsync()
        {
            var existing = await _regionRepository.GetAllAsync();
            if (existing.Any())
                return;

            foreach (var (name, lat, lng, radiusKm) in DefaultRegions)
            {
                await _regionRepository.AddAsync(new Region
                {
                    RegionName = name,
                    CenterLatitude = lat,
                    CenterLongitude = lng,
                    RadiusKm = radiusKm
                });
            }

            _logger.LogInformation("Seeded {Count} default regions.", DefaultRegions.Length);
        }
    }
}
