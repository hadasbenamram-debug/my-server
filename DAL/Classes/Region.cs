using DAL;
using DAL.Classes;
using System.ComponentModel.DataAnnotations;

namespace DAL.Classes
{
    public class Region
    {
        [Key]
        public int Id { get; set; }
        [Required, MaxLength(50)] public string RegionName { get; set; }

        public double CenterLatitude { get; set; }
        public double CenterLongitude { get; set; }
        public double RadiusKm { get; set; }

        public ICollection<Donation> Donations { get; set; }
        public ICollection<AvailabilitySlot> AvailabilitySlots { get; set; }

        // נוסף: תואם ל-Ngo.RegionId החדש.
        public ICollection<Ngo> Ngos { get; set; }
    }

}