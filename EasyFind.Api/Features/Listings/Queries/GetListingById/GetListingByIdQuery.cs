using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Listings;
using MediatR;

namespace EasyFind.Api.Features.Listings.Queries.GetListingById;

// A query carries its inputs and declares what it returns (via IRequest<T>)
public record GetListingByIdQuery(Guid Id, string userId) : IRequest<Result<ListingDetailDto>>;