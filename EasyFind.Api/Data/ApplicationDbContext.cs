using EasyFind.Api.Models.Admin;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Enum;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Models.Subscriptions;
using EasyFind.Api.Models.Users;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;


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
    public DbSet<OtpThrottle> OtpThrottles { get; set; }
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
            //entity.HasQueryFilter(l => l.DeletedAt == null);

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
            //
            // int[] is Npgsql-specific: other providers try to compose it with
            // their own collection-to-JSON converter and fail at model build.
            // So the enum lists fall back to a comma-separated string elsewhere,
            // which is what lets the integration tests run on SQLite.
            //
            // Fidelity note: those tests therefore exercise a different column
            // encoding than production for these two properties only. Anything
            // that depends on Postgres array semantics has to be tested against
            // Postgres.
            if (IsNpgsql)
            {
                entity.Property(p => p.PreferredJobCategories)
                    .HasConversion(
                        v => v.Select(c => (int)c).ToArray(),
                        v => v.Select(i => (JobCategory)i).ToList());

                entity.Property(p => p.PreferredScholarshipFields)
                    .HasConversion(
                        v => v.Select(f => (int)f).ToArray(),
                        v => v.Select(i => (ScholarshipField)i).ToList());
            }
            else
            {
                entity.Property(p => p.PreferredJobCategories)
                    .HasConversion(
                        v => string.Join(',', v.Select(c => (int)c)),
                        v => ParseEnumCsv<JobCategory>(v),
                        EnumListComparer<JobCategory>());

                entity.Property(p => p.PreferredScholarshipFields)
                    .HasConversion(
                        v => string.Join(',', v.Select(f => (int)f)),
                        v => ParseEnumCsv<ScholarshipField>(v),
                        EnumListComparer<ScholarshipField>());
            }

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

        // ── OtpThrottle ──────────────────────────────────
        modelBuilder.Entity<OtpThrottle>(entity =>
        {
            entity.HasKey(t => t.Id);

            // One row per number, and the lookup key for every check.
            // Required so the unique index actually constrains: Postgres allows
            // any number of NULLs in a unique index.
            entity.Property(t => t.PhoneNumber).IsRequired().HasMaxLength(32);
            entity.HasIndex(t => t.PhoneNumber).IsUnique();
        });

        // ── Non-Postgres providers: make DateTimeOffset sortable ────────────
        //
        // SQLite cannot ORDER BY a DateTimeOffset, and the feed orders by
        // CreatedAt, so the integration tests would fail on a limitation of the
        // test database rather than on anything real. Storing them as a binary
        // long keeps ordering correct there. Postgres is untouched.
        if (!IsNpgsql)
        {
            // Set the converter on the property itself. Going through
            // modelBuilder.Entity(clrType) would promote Identity's owned types
            // (IdentityPasskeyData) into keyless entities and fail validation.
            var toBinary = new DateTimeOffsetToBinaryConverter();

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) ||
                    property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(toBinary);
                }
            }
        }
    }

    private bool IsNpgsql => Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    // ── Non-Postgres fallbacks for the enum-array columns (see UserProfile) ──

    private static List<T> ParseEnumCsv<T>(string value) where T : struct, Enum
        => string.IsNullOrEmpty(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => (T)(object)int.Parse(s))
                .ToList();

    // Without an explicit comparer EF compares these lists by reference, and
    // would miss in-place edits to a profile's preferences.
    private static ValueComparer<List<T>> EnumListComparer<T>() where T : struct, Enum
        => new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            v => v.ToList());
}