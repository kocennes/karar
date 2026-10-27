using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Karar.Api.Common;
using Karar.Api.Contracts;

namespace Karar.UnitTests.Security;

// The request contracts are positional records: their DataAnnotations sit on constructor parameters,
// which Validator.TryValidateObject ignores. Before RequestValidation every rule below was dead, and
// e.g. POST /api/v1/auth/login with {} or POST /api/v1/auth/logout with {} returned 500.
public sealed class RequestValidationTests
{
    private static bool IsValid(object request, out List<ValidationResult> results)
    {
        results = new List<ValidationResult>();
        return RequestValidation.TryValidate(request, results);
    }

    [Fact]
    public void PlainTryValidateObject_IgnoresPositionalRecordAttributes()
    {
        // Documents the root cause: this is what ValidateRequest used to rely on.
        var request = new RefreshTokenRequest(null!);
        Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true)
            .Should().BeTrue();
    }

    [Fact]
    public void MissingRequiredField_IsRejected()
    {
        IsValid(new RefreshTokenRequest(null!), out var results).Should().BeFalse();
        results.Should().ContainSingle(r => r.MemberNames.Contains(nameof(RefreshTokenRequest.RefreshToken)));

        IsValid(new LoginRequest(null!, null!), out _).Should().BeFalse();
        IsValid(new LoginRequest("user", ""), out _).Should().BeFalse();
    }

    [Fact]
    public void RegisterRequest_EnforcesUsernamePatternEmailAndPasswordLength()
    {
        var dob = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        IsValid(new RegisterRequest("valid_name", "a@example.test", "long-enough-pw", dob, "other"), out _)
            .Should().BeTrue();

        IsValid(new RegisterRequest("bad name!", "a@example.test", "long-enough-pw", dob, "other"), out var r1)
            .Should().BeFalse();
        r1.Single().ErrorMessage.Should().Contain("Kullanıcı adı");

        IsValid(new RegisterRequest("valid_name", "not-an-email", "long-enough-pw", dob, "other"), out _)
            .Should().BeFalse();
        IsValid(new RegisterRequest("valid_name", "a@example.test", "short", dob, "other"), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void LengthAndRangeRules_AreEnforced()
    {
        IsValid(new CreatePostRequest("too short", new string('x', 60), 1), out _).Should().BeFalse();
        IsValid(new CreatePostRequest("long enough title", new string('x', 60), 0), out _).Should().BeFalse();
        IsValid(new CreatePostRequest("long enough title", new string('x', 60), 1), out _).Should().BeTrue();
        IsValid(new CreateReportRequest("post", Guid.NewGuid(), "spam", new string('x', 301)), out _).Should().BeFalse();
        IsValid(new TwoFactorDisableRequest(Password: new string('p', 73)), out _).Should().BeFalse();
    }

    [Fact]
    public void OptionalFields_MayBeOmitted()
    {
        IsValid(new TwoFactorDisableRequest(), out _).Should().BeTrue();
        IsValid(new CreateReportRequest("post", Guid.NewGuid(), "misinformation", null), out _).Should().BeTrue();
        IsValid(new PostViewRequest(), out _).Should().BeTrue();
    }

    [Fact]
    public void PropertyLevelAttributes_AreStillValidated_AndNotReportedTwice()
    {
        IsValid(new PropertyAnnotated { Name = null }, out var results).Should().BeFalse();
        results.Should().HaveCount(1);
    }

    private sealed class PropertyAnnotated
    {
        [Required]
        public string? Name { get; init; }
    }
}
