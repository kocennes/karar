using System.Net;
using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Karar.UnitTests.Privacy;

/// <summary>
/// Regression: uploaded post images were never deleted from the public bucket. Deleting a post (owner or
/// admin) only flipped posts.status, so the object stayed reachable at the same URL — cached for a year as
/// "immutable" — with no takedown path short of searching the bucket by hand.
/// </summary>
public sealed class PostImageLifecycleTests
{
    [Fact]
    public void StorageService_CanDeleteObjects()
    {
        typeof(IStorageService).GetMethod("DeleteAsync")
            .Should().NotBeNull("there must be a way to remove an uploaded image from public storage");
    }

    [Fact]
    public void CleanupJob_IsRegisteredAndPurgesImagesOfDeletedPosts()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        program.Should().Contain("builder.Services.AddHostedService<PostImageCleanupJob>();",
            "images of deleted posts must be purged by a durable, DB-driven job");

        TestRepoPaths.TryReadText(out var job, "backend", "Karar.Api", "Services", "PostImageCleanupJob.cs")
            .Should().BeTrue();
        job.Should().Contain("status = 'deleted'", "owner/admin deleted posts must lose their stored image");
        job.Should().Contain("image_moderation_status = 'rejected'",
            "a rejected image on a restored post can never be shown again and must not stay hosted");
        job.Should().Contain("SET image_url = NULL", "image_url is cleared only after the object is gone");
    }

    [Fact]
    public void GcsUploads_AreNotCachedAsImmutableForAYear()
    {
        var gcs = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "CloudStorageService.cs");

        gcs.Should().NotContain("immutable", "a takedown must propagate through CDN and browser caches");
        gcs.Should().NotContain("max-age=31536000");
    }

    // ── Behaviour ─────────────────────────────────────────────────────────

    private const string ObjectGuid = "0b1c2d3e-4f50-4172-8394-a5b6c7d8e9f0";

    [Theory]
    [InlineData("https://storage.googleapis.com/karar-images/posts/" + ObjectGuid + ".jpg")]
    [InlineData("https://cdn.karar.example/posts/" + ObjectGuid + ".jpg")]
    [InlineData("https://project.supabase.example/storage/v1/object/public/karar-images/posts/" + ObjectGuid + ".jpg")]
    public void TryGetObjectName_RecognisesUploadedPostImages(string imageUrl)
    {
        PostImageCleanupJob.TryGetObjectName(imageUrl, out var objectName).Should().BeTrue();
        objectName.Should().Be($"posts/{ObjectGuid}.jpg");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://storage.example/posts/" + ObjectGuid + ".jpg")]
    [InlineData("https://storage.example/posts/" + ObjectGuid + ".png")]
    [InlineData("https://storage.example/posts/" + ObjectGuid + ".jpg?alt=media")]
    [InlineData("https://storage.example/avatars/" + ObjectGuid + ".jpg")]
    [InlineData("https://storage.example/posts/not-a-guid.jpg")]
    [InlineData("https://storage.example/posts/" + ObjectGuid + ".jpg/../../admin/export.json")]
    public void TryGetObjectName_RefusesAnythingElse(string? imageUrl)
    {
        PostImageCleanupJob.TryGetObjectName(imageUrl, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Purge_DeletesObjectsAndClearsReferences_AndRetriesFailedDeletes()
    {
        var deletedPost = Guid.NewGuid();
        var failingPost = Guid.NewGuid();
        var foreignPost = Guid.NewGuid();
        var failingGuid = Guid.NewGuid();
        var storage = new FakeStorage(failObject: $"posts/{failingGuid}.jpg");
        var cleared = new List<Guid>();

        var purged = await PostImageCleanupJob.PurgeAsync(
            [
                (deletedPost, $"https://storage.googleapis.com/karar-images/posts/{ObjectGuid}.jpg"),
                (failingPost, $"https://project.supabase.example/storage/v1/object/public/karar-images/posts/{failingGuid}.jpg"),
                (foreignPost, "https://elsewhere.example/picture.jpg"),
            ],
            storage,
            (postId, _) => { cleared.Add(postId); return Task.CompletedTask; },
            NullLogger.Instance);

        storage.DeleteCalls.Should().Equal($"posts/{ObjectGuid}.jpg", $"posts/{failingGuid}.jpg");
        cleared.Should().Equal(deletedPost, foreignPost);
        cleared.Should().NotContain(failingPost, "image_url is kept so the next run retries the delete");
        purged.Should().Be(2);
    }

    [Fact]
    public async Task SupabaseDelete_UsesBulkRemoveEndpoint()
    {
        var http = new StubHttpClientFactory(_ => StubHttpClientFactory.Json(HttpStatusCode.OK, "[]"));
        var supabase = new SupabaseStorageService(SupabaseConfig(), NullLogger<SupabaseStorageService>.Instance, http);

        var deleted = await supabase.DeleteAsync($"posts/{ObjectGuid}.jpg");

        deleted.Should().BeTrue();
        http.Requests.Should().ContainSingle();
        var (request, body) = http.Requests[0];
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.ToString().Should().Be("https://project.supabase.example/storage/v1/object/karar-images");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        body.Should().Be($$"""{"prefixes":["posts/{{ObjectGuid}}.jpg"]}""");
    }

    [Fact]
    public async Task SupabaseDelete_ReportsFailureSoTheJobRetries()
    {
        var http = new StubHttpClientFactory(_ => StubHttpClientFactory.Json(HttpStatusCode.InternalServerError, "{}"));
        var supabase = new SupabaseStorageService(SupabaseConfig(), NullLogger<SupabaseStorageService>.Instance, http);

        (await supabase.DeleteAsync($"posts/{ObjectGuid}.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task DisabledStorage_DoesNotClaimDeletion()
    {
        var empty = new ConfigurationBuilder().Build();
        var http = new StubHttpClientFactory(_ => StubHttpClientFactory.Json(HttpStatusCode.OK, "[]"));

        (await new SupabaseStorageService(empty, NullLogger<SupabaseStorageService>.Instance, http)
            .DeleteAsync($"posts/{ObjectGuid}.jpg")).Should().BeFalse();
        (await new CloudStorageService(empty, NullLogger<CloudStorageService>.Instance)
            .DeleteAsync($"posts/{ObjectGuid}.jpg")).Should().BeFalse();
        http.Requests.Should().BeEmpty();
    }

    private static IConfiguration SupabaseConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://project.supabase.example/",
                ["Supabase:Key"] = "unit-test-placeholder",
                ["Supabase:Bucket"] = "karar-images",
            })
            .Build();

    private sealed class FakeStorage(string failObject) : IStorageService
    {
        public List<string> DeleteCalls { get; } = [];

        public bool IsEnabled => true;

        public Task<(string PublicUrl, string StorageUri)?> UploadAsync(
            Stream stream, string contentType, string objectName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string objectName, CancellationToken ct = default)
        {
            DeleteCalls.Add(objectName);
            return Task.FromResult(objectName != failObject);
        }
    }
}
