using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class AuthOtpFlowTests
{
    [Fact]
    public void ResetPassword_DeletesRedisOtpCacheAfterSuccessfulConsumption()
    {
        var programText = ReadProgram();
        var resetBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/auth/reset-password\"",
            "app.MapGet(\"/api/v1/admin/analytics/cache\"");

        resetBlock.Should().Contain("RedisService redis");
        resetBlock.Should().Contain("DELETE FROM email_otps WHERE email = @email");
        resetBlock.Should().Contain("await transaction.CommitAsync();");
        resetBlock.Should().Contain("KeyDeleteAsync($\"otp:pwreset:{email}\")");
    }

    [Fact]
    public void ChangeEmailConfirm_DeletesRedisOtpCacheAfterSuccessfulConsumption()
    {
        var programText = ReadProgram();
        var confirmBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/auth/change-email/confirm\"",
            "app.MapPost(\"/api/v1/auth/recover-account\"");

        confirmBlock.Should().Contain("RedisService redis");
        confirmBlock.Should().Contain("DELETE FROM email_otps WHERE email = @key");
        confirmBlock.Should().Contain("await tx.CommitAsync();");
        confirmBlock.Should().Contain("KeyDeleteAsync($\"otp:chgemail:{newEmail}\")");
    }

    private static string ReadProgram()
    {
        return TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
    }

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }

}
