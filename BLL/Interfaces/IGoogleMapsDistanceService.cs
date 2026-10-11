using System.Threading.Tasks;

namespace BLL.Integrations.GoogleMaps
{
    public interface IGoogleMapsDistanceService
    {
        Task<(double DistanceKm, double DurationMinutes)> GetDrivingDistanceAsync(
            double originLat, double originLng,
            double destLat, double destLng);
    }
}