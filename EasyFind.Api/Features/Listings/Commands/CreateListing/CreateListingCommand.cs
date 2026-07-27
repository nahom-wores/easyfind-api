using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Dto.Listings;
using MediatR;

namespace EasyFind.Api.Features.Listings.Commands.CreateListing;

public class CreateListingCommand : IRequest<Result<AdminListingDto>>
{
    public CreateListingDto CreateListingDto { get; set; }
}