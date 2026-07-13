using EasyFind.Api.Models.Dto.Profile;
using EasyFind.Api.Models.Users;
using FluentValidation;

namespace EasyFind.Api.Validators;

public class OnboardingValidator : AbstractValidator<OnboardingDto>
{
    public OnboardingValidator()
    {
        

        RuleFor(x => x.TargetCountries)
            .NotEmpty().WithMessage("Select at least one target country.");

        RuleForEach(x => x.TargetCountries)
            .Length(2).WithMessage("Country codes must be 2-letter ISO codes.");

        RuleFor(x => x.WorkExperienceYears)
            .InclusiveBetween((short)0, (short)50);

        // If they want jobs, they must pick at least one job category
        RuleFor(x => x.PreferredJobCategories)
            .NotEmpty()
            .When(x => x.SeekingType is SeekingType.Job or SeekingType.Both)
            .WithMessage("Select at least one job category.");

        // If they want scholarships, require field + degree level
        RuleFor(x => x.PreferredScholarshipFields)
            .NotEmpty()
            .When(x => x.SeekingType is SeekingType.Scholarship or SeekingType.Both)
            .WithMessage("Select at least one scholarship field.");

        RuleFor(x => x.TargetDegreeLevel)
            .NotNull()
            .When(x => x.SeekingType is SeekingType.Scholarship or SeekingType.Both)
            .WithMessage("Select your target degree level.");
        // Category cap
        RuleFor(x => x.PreferredJobCategories)
            .Must(c => c.Count <= 5).WithMessage("Select up to 5 categories.")
            .When(x => x.SeekingType is SeekingType.Job or SeekingType.Both);

       
    }
}