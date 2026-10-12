using AutoMapper;
using DAL;
using DAL.Classes;
using FoodRescue.DTOs;
using DTOs.DTOs; // ודא שמרחב השמות של CommentDto1 ממופה כאן

namespace BLL.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // מיפוי תרומות (התאמה ל-DonationResponseDto)
            CreateMap<Donation, DonationResponseDto>()
                .ForMember(d => d.Categories, o => o.MapFrom(s => s.DonationFoodCategories))
                .ForMember(d => d.NgoId, o => o.Ignore())
                .ReverseMap();
            CreateMap<CreateDonationDto, Donation>()
                .ForMember(d => d.DonationFoodCategories, o => o.MapFrom(s => s.Categories));
            CreateMap<DonationCategoryLineDto, DonationFoodCategory>().ReverseMap();
            CreateMap<User, UserDto>();
            // מיפוי משבצות זמינות
            CreateMap<AvailabilitySlot, AvailabilitySlotResponseDto>()
                .ForMember(d => d.Region, o => o.Ignore())
                .ForMember(d => d.ToRegion, o => o.Ignore())
                .ReverseMap();

            // מיפוי מתנדבים
            CreateMap<Volunteer, VolunteerResponseDto>().ReverseMap();
            CreateMap<CreateVolunteerDto, Volunteer>();

            // מיפוי ספקים
            CreateMap<Supplier, SupplierResponseDto>().ReverseMap();
            CreateMap<CreateSupplierDto, Supplier>();

            // מיפוי עמותות (NGOs)
            CreateMap<Ngo, NgoResponseDto>().ReverseMap();
            CreateMap<CreateNgoDto, Ngo>();

            // מיפוי בקשות אישור
            CreateMap<ApprovalRequest, ApprovalRequestResponseDto>().ReverseMap();
            CreateMap<CreateApprovalRequestDto, ApprovalRequest>();

            // מיפוי שידוכים
            CreateMap<Match, MatchResponseDto>()
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => s.AssignedAt));

            // מיפוי לוגי משלוח
            CreateMap<DeliveryLog, DeliveryLogResponseDto>()
                .ForMember(d => d.Status, o => o.MapFrom(s => s.StatusDescription))
                .ForMember(d => d.Notes, o => o.Ignore());
            CreateMap<CreateDeliveryLogDto, DeliveryLog>()
                .ForMember(d => d.StatusDescription, o => o.MapFrom(s =>
                    string.IsNullOrWhiteSpace(s.Notes) ? s.Status : s.Status + ": " + s.Notes));

            // מיפוי משובים
            CreateMap<Feedback, FeedbackResponseDto>().ReverseMap();
            CreateMap<CreateFeedbackDto, Feedback>();

            // 🟢 הוספת המיפוי החסר לתגובות:
            CreateMap<CommentDto1, Comment>().ReverseMap();
        }
    }
}