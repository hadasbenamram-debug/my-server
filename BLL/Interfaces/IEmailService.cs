using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(int? userId, string toEmail, string subject, string body);
    }
}