using System.Net;
using System.Net.Http.Json;
using EasyFind.Api.Models.Auth;
using FluentAssertions;

namespace EasyFind.IntegrationTests;

// Two things here.
//
// 1. The FluentValidation validators are actually enforced. They existed for
//    months without being registered or called, so none of the conditional
//    rules ran.
// 2. Every failure comes back in the ApiResponse envelope. There used to be
//    three shapes: ApiResponse, the framework's ProblemDetails for DataAnnotation
//    failures, and ProblemDetails again for unhandled exceptions.
public class ValidationAndErrorShapeTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private Task<(HttpClient client, ApplicationUser user)> AdminAsync()
        => factory.SignedInUserAsync(role: AppRoles.Admin);

    private static object ValidJobListing(object? overrides = null) => new
    {
        type = 0,                 // Job
        title = "Backend Engineer",
        organization = "Acme Corp",
        countryCode = "DE",
        description = "A real description",
        applyUrl = "https://apply.example.com",
        jobCategory = 1,
        employmentType = 1,
    };

    [Fact]
    public async Task JobListing_WithoutJobCategory_IsRejected()
    {
        var (admin, _) = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/admin/listings", new
        {
            type = 0,
            title = "Backend Engineer",
            organization = "Acme Corp",
            countryCode = "DE",
            description = "A real description",
            applyUrl = "https://apply.example.com",
            employmentType = 1,
            // jobCategory deliberately missing
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorsAsync()).Should().Contain("job category");
    }

    [Fact]
    public async Task JobListing_CarryingScholarshipFields_IsRejected()
    {
        var (admin, _) = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/admin/listings", new
        {
            type = 0,
            title = "Backend Engineer",
            organization = "Acme Corp",
            countryCode = "DE",
            description = "A real description",
            applyUrl = "https://apply.example.com",
            jobCategory = 1,
            employmentType = 1,
            scholarshipField = 2,   // not valid on a job
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorsAsync()).Should().Contain("scholarship field");
    }

    [Fact]
    public async Task Listing_WithDeadlineInThePast_IsRejectedOnCreate()
    {
        var (admin, _) = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/admin/listings", new
        {
            type = 0,
            title = "Backend Engineer",
            organization = "Acme Corp",
            countryCode = "DE",
            description = "A real description",
            applyUrl = "https://apply.example.com",
            jobCategory = 1,
            employmentType = 1,
            deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorsAsync()).Should().Contain("Deadline");
    }

    [Fact]
    public async Task ExpiredListing_CanStillBeEdited()
    {
        // The update validator deliberately drops the deadline-in-past rule.
        // Inheriting it would make an expired listing uneditable — an admin
        // could not fix a typo on it or take it down.
        var (admin, _) = await AdminAsync();

        var created = await admin.PostAsJsonAsync("/api/v1/admin/listings", ValidJobListing());
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        var id = (await created.ReadResultAsync<CreatedListing>()).Id;

        var edited = await admin.PutAsJsonAsync($"/api/v1/admin/listings/{id}", new
        {
            type = 0,
            title = "Backend Engineer (closed)",
            organization = "Acme Corp",
            countryCode = "DE",
            description = "A real description",
            applyUrl = "https://apply.example.com",
            jobCategory = 1,
            employmentType = 1,
            deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        });

        edited.StatusCode.Should().Be(HttpStatusCode.OK,
            "editing a listing whose deadline has passed must remain possible");
    }

    [Fact]
    public async Task ValidListing_IsAccepted()
    {
        // Guards against the validators being so strict that nothing gets through.
        var (admin, _) = await AdminAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/admin/listings", ValidJobListing());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DataAnnotationFailures_UseTheApiResponseEnvelope()
    {
        // [ApiController] would answer this with ProblemDetails by default.
        // LogInRequestDto has a [RegularExpression] on the phone number.
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/request-otp",
            new { phoneNumber = "not-a-phone-number" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("isSuccess",
            "model-binding failures must use the same envelope as everything else");
        body.Should().NotContain("\"traceId\"", "that would be ProblemDetails");

        (await response.ReadErrorsAsync()).Should().NotBeEmpty();
    }

    private class CreatedListing
    {
        public Guid Id { get; set; }
    }
}
