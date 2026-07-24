using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Models.Users;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace EasyFind.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }
    public DbSet<ApplicationUser> ApplicationUsers { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Listing> Listings { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<Bookmark> Bookmarks { get; set; }
    public DbSet<UserApplication> UserApplications { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<UserDocument> UserDocuments { get; set; }
    public DbSet<AdminAction> AdminActions { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(e => e.UserName).IsUnique();
            entity.Property(e => e.SubscriptionTier).HasConversion<int>();
        });
        modelBuilder.Entity<Listing>(entity =>
        {
            entity.HasKey(l => l.Id);

            // Store enums as int (default, but explicit for clarity)
            entity.Property(l => l.Type).HasConversion<int>();
            entity.Property(l => l.JobCategory).HasConversion<int>();
            entity.Property(l => l.EmploymentType).HasConversion<int>();
            entity.Property(l => l.ScholarshipField).HasConversion<int>();
            entity.Property(l => l.DegreeLevel).HasConversion<int>();
            entity.Property(l => l.FundingType).HasConversion<int>();
            entity.Property(l => l.SalaryPeriod).HasConversion<int>();
            entity.Property(l => l.SalaryCurrency).HasConversion<int>();
            // Soft-delete filter: every query auto-excludes deleted rows
            entity.HasQueryFilter(l => l.DeletedAt == null);

            // Indexes for feed performance
            entity.HasIndex(l => new { l.Type, l.CountryCode, l.IsActive });
            entity.HasIndex(l => l.JobCategory);
            entity.HasIndex(l => l.ScholarshipField);
            entity.HasIndex(l => l.Deadline);
            entity.HasIndex(l => l.IsFeatured);
        });
        // ── UserProfile ──────────────────────────────────
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(p => p.Id);

            // One profile per user
            entity.HasOne(p => p.User)
                .WithOne()
                .HasForeignKey<UserProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(p => p.UserId).IsUnique();
            entity.Property(p => p.ExperienceRange).HasConversion<int>();
            entity.Property(p => p.SeekingType).HasConversion<int>();
            entity.Property(p => p.EducationLevel).HasConversion<int>();
            entity.Property(p => p.TargetDegreeLevel).HasConversion<int>();
            entity.Property(p => p.Sex).HasConversion<int>();
            entity.Property(p => p.PassportStatus).HasConversion<int>();
            entity.Property(p => p.EnglishTestType).HasConversion<int>();
            
            // Postgres array columns — List<T> maps to native arrays.
            // Enum lists need explicit int[] conversion.
            entity.Property(p => p.PreferredJobCategories)
                .HasConversion(
                    v => v.Select(c => (int)c).ToArray(),
                    v => v.Select(i => (JobCategory)i).ToList());

            entity.Property(p => p.PreferredScholarshipFields)
                .HasConversion(
                    v => v.Select(f => (int)f).ToArray(),
                    v => v.Select(i => (ScholarshipField)i).ToList());
            
            // TargetCountries is List<string> — maps to text[] natively,
            // no conversion needed.
        });
        // ── Bookmark ─────────────────────────────────────
        modelBuilder.Entity<Bookmark>(entity =>
        {
            entity.HasKey(b => b.Id);

            // A user can't bookmark the same listing twice
            entity.HasIndex(b => new { b.UserId, b.ListingId }).IsUnique();

            entity.HasOne(b => b.Listing)
                .WithMany()
                .HasForeignKey(b => b.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK to Identity user
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

// ── UserApplication ──────────────────────────────
        modelBuilder.Entity<UserApplication>(entity =>
        {
            entity.HasKey(a => a.Id);

            // One tracker entry per user per listing
            entity.HasIndex(a => new { a.UserId, a.ListingId }).IsUnique();

            entity.Property(a => a.Status).HasConversion<int>();

            entity.HasOne(a => a.Listing)
                .WithMany()
                .HasForeignKey(a => a.ListingId)
                .OnDelete(DeleteBehavior.Restrict);   // don't delete tracker if listing soft-deleted

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // Subscription
        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Tier).HasConversion<int>();
            entity.Property(s => s.Status).HasConversion<int>();
            entity.HasIndex(s => new { s.UserId, s.Status, s.ExpiresAt });
            entity.HasOne(s => s.User).WithMany()
                .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        
        //Payment
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Tier).HasConversion<int>();
            entity.Property(p => p.Status).HasConversion<int>();
            entity.Property(p => p.Provider).HasConversion<int>();
            entity.Property(p => p.TxRef).HasMaxLength(100).IsRequired();
            entity.HasIndex(p => p.TxRef).IsUnique();   // the matching key — must be unique
            entity.HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        // UserDoc
        modelBuilder.Entity<UserDocument>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Type).HasConversion<int>();
            entity.Property(d => d.FileName).HasMaxLength(255);
            entity.Property(d => d.StorageKey).HasMaxLength(500);
            entity.HasIndex(d => d.UserId);   // fetch all of a user's docs
            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        
        // Admin
        modelBuilder.Entity<AdminAction>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.ActionType).HasConversion<int>();
            entity.Property(a => a.Details).HasMaxLength(1000);
            entity.Property(a => a.Reason).HasMaxLength(500);
            entity.HasIndex(a => a.TargetUserId);
            entity.HasIndex(a => a.AdminUserId);
            entity.HasIndex(a => a.CreatedAt);
            entity.HasOne(a => a.AdminUser).WithMany()
                .HasForeignKey(a => a.AdminUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}