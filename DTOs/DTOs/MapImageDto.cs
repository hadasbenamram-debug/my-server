using System;

namespace FoodRescue.DTOs
{
    // תמונת מפה שהשרת הביא מ-Google. הקונטרולר מחזיר אותה כקובץ (למשל image/png).
    public class MapImageDto
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "image/png";
    }
}
