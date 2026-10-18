using AutoMapper;
using BLL.Interfaces;
using DAL.Interfaces;
using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Services
{
    // כל הישויות (ספקים, עמותות ומתנדבים) לתמונת המצב המרחבית במפה.
    public class MapService : IMapService
    {
        private readonly ISupplierRepository _supplierRepository;
        private readonly INgoRepository _ngoRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly IMapper _mapper;

        public MapService(
            ISupplierRepository supplierRepository,
            INgoRepository ngoRepository,
            IVolunteerRepository volunteerRepository,
            IMapper mapper)
        {
            _supplierRepository = supplierRepository;
            _ngoRepository = ngoRepository;
            _volunteerRepository = volunteerRepository;
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
    }
}
