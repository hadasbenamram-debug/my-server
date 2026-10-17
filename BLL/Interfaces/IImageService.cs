using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Interfaces
{
    public interface IImageService
    {
        Task<string> Save(
            IFormFile image,
            CancellationToken cancellationToken = default);

        Task Delete(string? fileName);
    }
}
