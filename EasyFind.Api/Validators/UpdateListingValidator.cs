using EasyFind.Api.Models.Dto.Listings;

namespace EasyFind.Api.Validators;

// Every create rule EXCEPT "deadline cannot be in the past".
//
// Inheriting that rule would make an expired listing uneditable — an admin could
// not fix a typo on it, or take it down — which is why this is not simply
// `: CreateListingValidator`.
public class UpdateListingValidator : ListingRules<UpdateListingDto>
{
}
