using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services.IServices;

namespace EasyFind.Api.Features.Listings;

// The single entry point to listing data. Every read of db.Listings goes through
// here so that "who can see what" has exactly one definition.
//
// Visibility is enforced explicitly rather than through a global EF query filter:
// the filter in ApplicationDbContext is disabled, so a query that skips this
// service sees soft-deleted and inactive rows.
public class ListingAuthorizationService
{
    private readonly ApplicationDbContext db;
    private readonly ICurrentUser currentUser;

    public ListingAuthorizationService(
        ApplicationDbContext db,
        ICurrentUser currentUser)
    {
        this.db = db ?? throw new ArgumentNullException(nameof(db));
        this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    // Internal staff: may see inactive and soft-deleted listings.
    public bool IsManager =>
        currentUser.IsInRole(AppRoles.SuperAdmin) || currentUser.IsInRole(AppRoles.Admin);

    // Listings the current user may READ, as an IQueryable — nothing has hit the
    // database yet, so callers can still filter, page and project.
    //
    // activeOnly: false keeps listings the user has already saved or applied to
    // visible after they go inactive (an expired job stays in your tracker).
    // Soft-deleted listings are pulled from circulation and never come back.
    public IQueryable<Listing> AuthorizedListings(bool activeOnly = true)
    {
        if (IsManager)
            return db.Listings;

        // A partner's staff would see only their own organization's listings:
        // if (currentUser.IsInRole(AppRoles.Partner))
        //     return db.Listings.Where(l => l.OrganizationId == currentUser.OrganizationId);

        // Unknown or missing role → sees nothing (safe default).
        if (!currentUser.IsInRole(AppRoles.User))
            return db.Listings.Where(_ => false);

        var visible = db.Listings.Where(l => l.DeletedAt == null);
        return activeOnly ? visible.Where(l => l.IsActive) : visible;
    }

    // Listings the current user may MANAGE — includes inactive and soft-deleted
    // rows. Tracked (not AsNoTracking), so callers can mutate and SaveChanges.
    // Empty for non-staff, so a missing [Authorize] can't turn into a data leak.
    public IQueryable<Listing> ManageableListings()
        => IsManager ? db.Listings : db.Listings.Where(_ => false);
}
