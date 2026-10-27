using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Endpoints that require a caller must resolve it (device token or JWT) before ValidateRequest:
// an anonymous caller gets 401, not a 400 that describes the expected body. This only became
// observable once ValidateRequest enforced the record-parameter DataAnnotations (RequestValidation);
// before that, these handlers validated first but validation never failed.
public sealed class AuthBeforeValidationContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Theory]
    [InlineData("app.MapPut(\"/api/v1/devices/fcm-token\"")]
    [InlineData("app.MapPost(\"/api/v1/posts\"")]
    [InlineData("app.MapPut(\"/api/v1/posts/{id:guid}\"")]
    [InlineData("app.MapPost(\"/api/v1/posts/discover/events\"")]
    [InlineData("app.MapPost(\"/api/v1/posts/{id:guid}/vote\"")]
    [InlineData("app.MapPut(\"/api/v1/comments/{id:guid}\"")]
    [InlineData("app.MapPost(\"/api/v1/reports\"")]
    [InlineData("app.MapPost(\"/api/v1/auth/register\"")]
    [InlineData("app.MapPost(\"/api/v1/auth/2fa/enable\"")]
    [InlineData("app.MapPut(\"/api/v1/users/me/password\"")]
    [InlineData("app.MapPost(\"/api/v1/auth/change-email/request\"")]
    [InlineData("app.MapPost(\"/api/v1/auth/change-email/confirm\"")]
    public void CallerIsResolved_BeforeTheBodyIsValidated(string endpointStart)
    {
        var block = EndpointBlock(endpointStart);

        var validate = block.IndexOf("ValidateRequest(", StringComparison.Ordinal);
        var identity = new[]
            {
                block.IndexOf("requestDevice.TryGetDeviceIdAsync(", StringComparison.Ordinal),
                block.IndexOf("GetJwtPrincipal(", StringComparison.Ordinal),
            }
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        var unauthorized = block.IndexOf("Unauthorized()", StringComparison.Ordinal);

        validate.Should().BeGreaterThan(0, "the endpoint validates its body");
        identity.Should().BeGreaterThan(0, "the endpoint resolves its caller");
        identity.Should().BeLessThan(validate);
        unauthorized.Should().BeLessThan(validate, "the 401 branch must come before the 400 branch");
    }

    private static string EndpointBlock(string start)
    {
        var from = ProgramText.IndexOf(start + ", async (", StringComparison.Ordinal);
        from.Should().BeGreaterThanOrEqualTo(0, $"{start} should exist");
        var to = ProgramText.IndexOf("\napp.Map", from + start.Length, StringComparison.Ordinal);
        return to < 0 ? ProgramText[from..] : ProgramText[from..to];
    }
}
