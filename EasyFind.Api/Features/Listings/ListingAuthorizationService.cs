using EasyFind.Api.Data;
using EasyFind.Api.Models.Auth;
using EasyFind.Api.Models.Listings;
using EasyFind.Api.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace EasyFind.Api.Features.Listings;

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
    
    
    // The single entry point to listing data.
    // Returns ONLY the listings the current user is allowed to see — as an
    // IQueryable, so nothing has hit the database yet.
    public IQueryable<Listing> AuthorizedListings()
    {
        return currentUser.Role switch
        {
            // Internal admins see everything (including soft-deleted, for management)
            AppRoles.SuperAdmin => db.Listings.IgnoreQueryFilters(),
            AppRoles.Admin      => db.Listings,

            // A partner's staff see ONLY their own organization's listings
            // AppRoles.Partner => db.Listings
            //     .Where(l => l.OrganizationId == currentUser.OrganizationId),

            // Regular job-seekers see only active, public listings
            AppRoles.User => db.Listings
                .Where(l => l.IsActive),

            // Unknown role → see nothing (safe default)
            _ => db.Listings.Where(_ => false),
        };
    }
}