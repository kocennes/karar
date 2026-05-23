using FluentAssertions;
using Karar.Api.Common;
using System.Net;

namespace Karar.UnitTests.Security;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_MasksSensitiveQueryAndJsonValues()
    {
        var input =
            "/api/v1/auth/login?email=ada@example.com&device_id=device-123&otp=123456 {\"refreshToken\":\"abc.def\",\"password\":\"secret\"}";

        var result = SensitiveDataRedactor.Redact(input);

        result.Should().NotContain("ada@example.com");
        result.Should().NotContain("device-123");
        result.Should().NotContain("123456");
        result.Should().NotContain("abc.def");
        result.Should().NotContain("secret");
        result.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void RedactHeader_MasksAuthorizationAndDeviceTokenHeaders()
    {
        SensitiveDataRedactor
            .RedactHeader("Authorization", "Bearer eyJhbGciOiJIUzI1NiJ9.payload")
            .Should()
            .Be("[REDACTED]");

        SensitiveDataRedactor
            .RedactHeader("X-Device-Token", "raw-device-token")
            .Should()
            .Be("[REDACTED]");
    }

    [Fact]
    public void RedactIp_ReducesIpv4ToSubnet()
    {
        var result = SensitiveDataRedactor.RedactIp(IPAddress.Parse("203.0.113.42"));

        result.Should().Be("203.0.113.0/24");
    }
}
