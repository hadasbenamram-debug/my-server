using System;
using System.Threading;
using System.Threading.Tasks;

namespace DAL.Interfaces
{
    // כל Repository קורא ל-SaveChanges בעצמו, ולכן כשה-Service צריך שכמה פעולות
    // יצליחו יחד או יתבטלו יחד (טרנזקציה), הוא עוטף אותן ב-ExecuteInTransactionAsync.
    public interface IUnitOfWork
    {
        // מריץ את כל הפעולות בתוך טרנזקציה אחת.
        // מחזיר false אם הייתה התנגשות עדכון במקביל (Concurrency) - ואז הכול מבוטל.
        // כל חריגה אחרת מבטלת את הטרנזקציה ונזרקת הלאה.
        Task<bool> ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default);
    }
}
