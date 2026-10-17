using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface ICommentService
    {
        // עץ התגובות: מנהל רואה הכל, כל משתמש אחר רק תגובות על תרומות/שידוכים שהוא קשור אליהם
        Task<List<CommentDto>> GetTreeAsync(CurrentUserDto currentUser);

        Task<CommentDto> AddAsync(CreateCommentDto dto, CurrentUserDto currentUser);

        // כל התגובות כרשימה שטוחה (מנהל)
        Task<List<CommentDto>> GetAllAsync();

        // מחיקת תגובה וכל התגובות שמתחתיה (מנהל)
        Task<int> DeleteAsync(int id);
    }
}
