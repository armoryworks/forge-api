using System.Net;
using System.Text;

namespace Forge.Tests.Helpers;

/// <summary>
/// A route-aware fake <see cref="HttpMessageHandler"/> for carrier contract tests: returns the canned
/// JSON body whose key is a substring of the request URL (e.g. "/oauth/token", "/rate/v1/"). Pairs with
/// <see cref="StubHttpClientFactory"/> so a real carrier adapter (which takes IHttpClientFactory) can be
/// driven against canned responses with no network or sandbox — the reusable carrier-by-carrier harness.
/// </summary>
public sealed class RouteStubHandler(IReadOnlyDictionary<string, string> routes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.AbsoluteUri;
        var body = routes.FirstOrDefault(kv => url.Contains(kv.Key)).Value ?? "{}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
