using DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class OrphanImageCleanupService
    {
        private readonly IDonationRepository _donationRepository;
        private readonly string _imagesFolder;
        private readonly ILogger<OrphanImageCleanupService> _logger;

        public OrphanImageCleanupService(
            IDonationRepository donationRepository,
            string imagesFolder,
            ILogger<OrphanImageCleanupService> logger)
        {
            _donationRepository = donationRepository;
            _imagesFolder = Path.GetFullPath(imagesFolder);
            _logger = logger;
        }

        public async Task CleanupAsync()
        {
            if (!Directory.Exists(_imagesFolder))
                return;

            var referenced = (await _donationRepository.GetImageFilePathsAsync())
                .Select(p => Path.GetFileName(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var threshold = DateTime.UtcNow.AddHours(-1);

            foreach (var file in Directory.EnumerateFiles(_imagesFolder))
            {
                var name = Path.GetFileName(file);

                if (referenced.Contains(name))
                    continue;

                if (File.GetCreationTimeUtc(file) > threshold)
                    continue;

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete orphan image.");
                }
            }
        }
    }
}
