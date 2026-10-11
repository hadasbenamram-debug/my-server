public class UpdateNgoHoursDto
{
    public string Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public TimeSpan? SunToThuOpenTime { get; set; }
    public TimeSpan? SunToThuCloseTime { get; set; }
    public bool ReceivesOnFriday { get; set; }
    public TimeSpan? FridayOpenTime { get; set; }
    public TimeSpan? FridayCloseTime { get; set; }
}