using System.Net;

namespace Karar.UnitTests;

/// <summary>
/// IHttpClientFactory whose clients never touch the network: every request is recorded and answered
/// by <paramref name="respond"/>.
/// </summary>
internal sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public HttpClient CreateClient(string name) => new(new Handler(this, respond));

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Handler(StubHttpClientFactory owner, Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            owner.Requests.Add((request, body));
            return respond(request);
        }
    }
}
