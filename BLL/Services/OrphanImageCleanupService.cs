using BLL.Interfaces;
using DAL.Interfaces;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    // ניקוי תקופתי של תמונות שהועלו (analyze-image) אבל לא שויכו לאף תרומה.
    // השירות לא נוגע בקבצים בעצמו: את רשימת הקבצים ואת המחיקה מבצע IImageService.
    public class OrphanImageCleanupService
    {
        // תמונה שהועלתה זה עתה עדיין יכולה להישלח בטופס יצירת התרומה
        private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(1);

        private readonly IDonationRepository _donationRepository;
        private readonly IImageService _imageService;
        private readonly TimeProvider _clock;

        public OrphanImageCleanupService(IDonationRepository donationRepository, IImageService imageService, TimeProvider? clock = null)
        {
            _donationRepository = donationRepository;
            _imageService = imageService;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task CleanupAsync()
        {
            var referenced = (await _donationRepository.GetImageFileNamesAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var fileName in _imageService.ListFileNames(_clock.GetUtcNow().UtcDateTime - GracePeriod))
            {
                if (!referenced.Contains(fileName))
                    await _imageService.Delete(fileName);
            }
        }
    }
}
