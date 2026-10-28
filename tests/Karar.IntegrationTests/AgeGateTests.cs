using System.Net;
using System.Net.Http.Json;
using Karar.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Karar.IntegrationTests;

// P0-02 neutral age gate. These tests need no database: they cover the wiring that runs
// before any SQL (flag defaults, identity checks). The SQL paths are covered end to end in
// AgeGateRealStackTests (Category=RequiresDb).
[Collection("ApiTests")]
public class AgeGateTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AgeGateTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void Flags_AreOffByDefault()
    {
        var gate = _factory.Services.GetRequiredService<AgeGateService>();
        Assert.False(gate.Enforce);
        Assert.False(gate.GuestVote);

        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        Assert.False(configuration.GetValue("AgeGate:Enforce", true));
        Assert.False(configuration.GetValue("AgeGate:GuestVote", true));
    }

    [Fact]
    public async Task DeviceAgeDeclaration_WithoutDeviceToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/devices/age-declaration",
            new { dateOfBirth = "1990-01-01" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Onboarding_WithoutJwt_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/users/me/onboarding",
            new { dateOfBirth = "1990-01-01", acceptedTerms = true, acceptedCommunityGuidelines = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
