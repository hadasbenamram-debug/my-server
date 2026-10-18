using DAL.Classes;
using DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    // יצירת קטגוריות המזון הבסיסיות בעליית השרת, אם הטבלה ריקה.
    // (באותו דפוס כמו AdminSeedService.) אם כבר יש קטגוריות במסד - לא נוגעים בהן.
    public class FoodCategorySeedService
    {
        // PerishabilityFactor: כמה מהר המזון מתקלקל (משפיע על נוסחת הדחיפות). 1.0 = רגיל.
        private static readonly (string Name, double Perishability)[] DefaultCategories =
        {
            ("פירות וירקות", 1.3),
            ("מוצרי חלב וביצים", 1.5),
            ("לחם ומאפים", 1.2),
            ("בשר ועופות", 1.8),
            ("מזון יבש ושימורים", 0.5),
            ("משקאות", 0.6),
            ("מזון מוכן ותבשילים", 1.7),
        };

        private readonly IFoodCategoryRepository _categoryRepository;
        private readonly ILogger<FoodCategorySeedService> _logger;

        public FoodCategorySeedService(
            IFoodCategoryRepository categoryRepository,
            ILogger<FoodCategorySeedService> logger)
        {
            _categoryRepository = categoryRepository;
            _logger = logger;
        }

        public async Task SeedCategoriesAsync()
        {
            var existing = await _categoryRepository.GetAllAsync();
            if (existing.Any())
                return;

            foreach (var (name, perishability) in DefaultCategories)
            {
                await _categoryRepository.AddAsync(new FoodCategory
                {
                    CategoryName = name,
                    PerishabilityFactor = perishability
                });
            }

            _logger.LogInformation("Seeded {Count} default food categories.", DefaultCategories.Length);
        }
    }
}
