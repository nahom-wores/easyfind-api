using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;

namespace EasyFind.Api.Services.IServices;

public interface IBookmarkService
{
    Task<Result> AddAsync(string userId, Guid listingId, CancellationToken ct = default);
    Task<Result> RemoveAsync(string userId, Guid listingId, CancellationToken ct = default);
    Task<PagedResult<ListingFeedItemDto>> GetUserBookmarksAsync(string userId, int page, int pageSize, CancellationToken ct = default);
}