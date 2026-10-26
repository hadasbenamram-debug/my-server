using FoodRescue.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface ICommentService
    {
        // עץ התגובות: מנהל רואה הכל, כל משתמש אחר רק תגובות על תרומות שהוא קשור אליהן.
        // תיקון 18: role (לא חובה) - רק התרומות שהמשתמש קשור אליהן בתפקיד הזה (למשתמש עם כמה תפקידים)
        Task<List<CommentDto>> GetTreeAsync(CurrentUserDto currentUser, RequestedRole? role = null);

        Task<CommentDto> AddAsync(CreateCommentDto dto, CurrentUserDto currentUser);

        // כל התגובות כרשימה שטוחה (מנהל)
        Task<List<CommentDto>> GetAllAsync();

        // מחיקת תגובה וכל התגובות שמתחתיה (מנהל)
        Task<int> DeleteAsync(int id);
    }
}
