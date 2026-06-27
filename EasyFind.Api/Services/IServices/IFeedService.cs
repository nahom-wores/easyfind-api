using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;

namespace EasyFind.Api.Services.IServices;

public interface IFeedService
{
    Task<PagedResult<ListingFeedItemDto>> GetPersonalizedFeedAsync(
        string userId, FeedRequestDto request, CancellationToken ct = default);
}