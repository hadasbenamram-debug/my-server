using DAL.Interfaces;
using FoodRescue.DTOs;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IDonationService
    {
        // שומר את התמונה, זוכר שהיא שייכת למשתמש, ומנתח אותה בעזרת AI
        Task<ImageAnalysisResponseDto> AnalyzeImageAsync(int userId, IFormFile image, CancellationToken cancellationToken);

        Task<List<SupplierDonationSummaryDto>> GetMySupplierDonationsAsync(int userId);
        Task<IEnumerable<DonationResponseDto>> GetMyNgoDonationsAsync(CurrentUserDto currentUser);
        Task<PagedResult<AdminDonationSummaryDto>> GetAllForAdminAsync(PageRequest page);

        // מחזיר null אם התרומה לא קיימת או שאין למשתמש הרשאה לראות אותה
        Task<DonationResponseDto?> GetByIdAsync(int donationId, CurrentUserDto currentUser);

        Task<IEnumerable<DonationResponseDto>> GetAvailableAsync(CurrentUserDto currentUser);
        Task<DonationResponseDto> CreateAsync(CreateDonationDto dto, CurrentUserDto currentUser);

        // חדש: הספק (או מנהל) יכול לערוך תרומה כל עוד היא ממתינה לשיבוץ
        Task<DonationResponseDto> UpdateAsync(int donationId, UpdateDonationDto dto, CurrentUserDto currentUser);

        // חדש: גם הספק עצמו יכול לבטל (עד שהתרומה נאספה), לא רק מנהל
        Task CancelAsync(int donationId, CurrentUserDto currentUser, string? reason);

        // חדש: העמותה מאשרת קבלה. מחזיר את מזהה השידוך (לדירוג)
        Task<int> ConfirmReceiptAsync(int donationId, CurrentUserDto currentUser);
    }
}
