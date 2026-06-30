using EasyFind.Api.Models.Dto.Listings;
using EasyFind.Api.Models.Enum;
using FluentValidation;

namespace EasyFind.Api.Validators;

public class CreateListingValidator : AbstractValidator<CreateListingDto>
    {
        public CreateListingValidator()
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
            RuleFor(x => x.Organization).NotEmpty().MaximumLength(255);
            RuleFor(x => x.CountryCode).NotEmpty().Length(2)
                .WithMessage("Country code must be a 2-letter ISO code.");
            RuleFor(x => x.Description).NotEmpty();
            RuleFor(x => x.ApplyUrl).NotEmpty()
                .Must(BeAValidUrl).WithMessage("Apply URL must be a valid URL.");

            RuleFor(x => x.Deadline)
                .Must(d => d == null || d >= DateOnly.FromDateTime(DateTime.UtcNow))
                .WithMessage("Deadline cannot be in the past.");

            // Salary sanity
            RuleFor(x => x.SalaryMax)
                .GreaterThanOrEqualTo(x => x.SalaryMin)
                .When(x => x.SalaryMin.HasValue && x.SalaryMax.HasValue)
                .WithMessage("Max salary must be >= min salary.");

            // ── Type-discriminator integrity ──────────────────
            When(x => x.Type == ListingType.Job, () =>
            {
                RuleFor(x => x.JobCategory).NotNull()
                    .WithMessage("Job listings require a job category.");
                RuleFor(x => x.ScholarshipField).Null()
                    .WithMessage("Job listings cannot have a scholarship field.");
                RuleFor(x => x.DegreeLevel).Null();
                RuleFor(x => x.FundingType).Null();
            });

            When(x => x.Type == ListingType.Scholarship, () =>
            {
                RuleFor(x => x.ScholarshipField).NotNull()
                    .WithMessage("Scholarship listings require a field.");
                RuleFor(x => x.DegreeLevel).NotNull()
                    .WithMessage("Scholarship listings require a degree level.");
                RuleFor(x => x.JobCategory).Null()
                    .WithMessage("Scholarship listings cannot have a job category.");
                RuleFor(x => x.SalaryMin).Null();
                RuleFor(x => x.SalaryMax).Null();
            });
        }

        private static bool BeAValidUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var result)
            && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }