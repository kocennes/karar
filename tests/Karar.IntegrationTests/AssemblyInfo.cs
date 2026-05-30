using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// One shared WebApplicationFactory for the entire "ApiTests" collection.
// Without this, each test CLASS creates its own factory (8×), each blocking
// ~30s on the Redis ConnectionMultiplexer.Connect() call → 4+ minutes of startup.
[CollectionDefinition("ApiTests")]
public class ApiTestsCollection : ICollectionFixture<Karar.IntegrationTests.CustomWebApplicationFactory> { }
