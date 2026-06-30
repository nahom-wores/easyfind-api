using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(e => e.UserName).IsUnique();
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

            entity.Property(p => p.SeekingType).HasConversion<int>();
            entity.Property(p => p.EducationLevel).HasConversion<int>();
            entity.Property(p => p.TargetDegreeLevel).HasConversion<int>();
            entity.Property(p => p.Sex).HasConversion<int>();
            entity.Property(p => p.PassportStatus).HasConversion<int>();
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
    }
}