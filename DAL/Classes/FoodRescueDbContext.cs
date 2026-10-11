using DAL.Classes;
using Microsoft.EntityFrameworkCore;

namespace DAL
{
    public class FoodRescueDbContext : DbContext
    {
        public FoodRescueDbContext(DbContextOptions<FoodRescueDbContext> options) : base(options)
        {
        }

        // הגדרת כל הטבלאות (DbSets) במערכת בהתאם לכל המחלקות שהוזנו
        public DbSet<User> Users { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Ngo> Ngos { get; set; }
        public DbSet<Volunteer> Volunteers { get; set; }
        public DbSet<Manager> Managers { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<FoodCategory> FoodCategories { get; set; }
        public DbSet<DonationFoodCategory> DonationFoodCategories { get; set; }
        public DbSet<Donation> Donations { get; set; }
        public DbSet<Match> Matches { get; set; }
        public DbSet<VolunteerRoute> VolunteerRoutes { get; set; }
        public DbSet<DeliveryLog> DeliveryLogs { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<ApprovalRequest> ApprovalRequests { get; set; }
        public DbSet<AvailabilitySlot> AvailabilitySlots { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DonationFoodCategory>()
                .HasKey(dfc => new { dfc.DonationId, dfc.CategoryId });

            modelBuilder.Entity<Match>()
                .HasOne(m => m.Donation)
                .WithMany(d => d.Matches)
                .HasForeignKey(m => m.DonationId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Match>()
                .HasOne(m => m.Ngo)
                .WithMany(n => n.Matches)
                .HasForeignKey(m => m.NgoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AvailabilitySlot>()
                .HasOne(a => a.Region)
                .WithMany(r => r.AvailabilitySlots)
                .HasForeignKey(a => a.RegionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AvailabilitySlot>()
                .HasOne(a => a.ToRegion)
                .WithMany()
                .HasForeignKey(a => a.ToRegionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique constraint על אימייל
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // מניעת יותר מ-Match פעיל אחד לאותה תרומה (Unique filtered index)
            modelBuilder.Entity<Match>()
                .HasIndex(m => new { m.DonationId, m.IsActive })
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            // ===== תוספות: מונע יותר מפרופיל אחד מכל סוג לאותו משתמש =====
            modelBuilder.Entity<Manager>()
                .HasIndex(m => m.UserId)
                .IsUnique();

            modelBuilder.Entity<Volunteer>()
                .HasIndex(v => v.UserId)
                .IsUnique();

            modelBuilder.Entity<Supplier>()
                .HasIndex(s => s.UserId)
                .IsUnique();

            modelBuilder.Entity<Ngo>()
                .HasIndex(n => n.UserId)
                .IsUnique();
            // ================================================================

            // Indexes to speed login-attempt windowed queries
            modelBuilder.Entity<LoginAttempt>()
                .HasIndex(l => new { l.Email, l.AttemptedAt });

            modelBuilder.Entity<LoginAttempt>()
                .HasIndex(l => new { l.IpAddress, l.AttemptedAt });

            modelBuilder.Entity<LoginAttempt>()
                .HasOne(l => l.User)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}