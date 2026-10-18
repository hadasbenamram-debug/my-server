using System;
using System.ComponentModel.DataAnnotations;

namespace DAL.Classes
{
    public class LoginAttempt
    {
        [Key]
        public int Id { get; set; }

        // Optional FK to user if the email matched an existing user
        public int? UserId { get; set; }
        public User User { get; set; }

        [MaxLength(200)]
        public string Email { get; set; }

        // IPv6 max length
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

        public bool WasSuccessful { get; set; }

        [MaxLength(200)]
        public string? FailureReason { get; set; }
    }
}
