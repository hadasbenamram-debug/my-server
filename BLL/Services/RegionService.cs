using AutoMapper;
using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Classes;
using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Services
{
    public class RegionService : IRegionService
    {
        private const string RegionInUse = "לא ניתן למחוק את האזור משום שהוא משויך לרשומות אחרות במערכת.";

        private readonly IRegionRepository _regionRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IDonationRepository _donationRepository;
        private readonly IAvailabilitySlotRepository _availabilitySlotRepository;
        private readonly IMapper _mapper;
        private readonly ILogger<RegionService> _logger;

        public RegionService(
            IRegionRepository regionRepository,
            INgoRepository ngoRepository,
            IDonationRepository donationRepository,
            IAvailabilitySlotRepository availabilitySlotRepository,
            IMapper mapper,
            ILogger<RegionService> logger)
        {
            _regionRepository = regionRepository;
            _ngoRepository = ngoRepository;
            _donationRepository = donationRepository;
            _availabilitySlotRepository = availabilitySlotRepository;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<IEnumerable<RegionResponseDto>> GetAllAsync()
        {
            var regions = await _regionRepository.GetAllAsync();
            return _mapper.Map<IEnumerable<RegionResponseDto>>(regions.OrderBy(r => r.RegionName));
        }

        public async Task<RegionResponseDto?> GetByIdAsync(int id)
        {
            var region = await _regionRepository.GetByIdAsync(id);
            return region == null ? null : _mapper.Map<RegionResponseDto>(region);
        }

        public async Task DeleteAsync(int id)
        {
            var region = await _regionRepository.GetByIdAsync(id);
            if (region == null)
                throw new NotFoundException("האזור למחיקה לא נמצא.");

            // חוק עסקי: אי אפשר למחוק אזור שמשויך לעמותה, לתרומה או למשבצת זמינות
            bool isInUse =
                await _ngoRepository.AnyInRegionAsync(id) ||
                await _donationRepository.AnyInRegionAsync(id) ||
                await _availabilitySlotRepository.AnyInRegionAsync(id);

            if (isInUse)
                throw new ConflictException(RegionInUse);

            if (!await _regionRepository.TryDeleteAsync(region))
            {
                _logger.LogWarning("Failed to delete region {RegionId}.", id);
                throw new ConflictException(RegionInUse);
            }
        }

        // תוקן: הוחזר בעבר CreateRegionDto - בלי Id, ולכן אי אפשר היה לבחור אזור ברשימה
        public async Task<RegionResponseDto> AddAsync(CreateRegionDto dto)
        {
            var regionEntity = _mapper.Map<Region>(dto);

            // אבטחה: איפוס מזהה כדי למנוע דריסת ID מצד הלקוח
            regionEntity.Id = 0;
            regionEntity.RegionName = dto.RegionName.Trim();

            await _regionRepository.AddAsync(regionEntity);

            return _mapper.Map<RegionResponseDto>(regionEntity);
        }
    }
}
