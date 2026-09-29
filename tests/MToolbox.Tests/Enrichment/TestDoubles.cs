using System.Net;
using System.Text;
using MToolbox.Core.Enrichment;

namespace MToolbox.Tests.Enrichment;

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class FakeTokenStore(string? github = null, string? devops = null) : ITokenStore
{
    private readonly Dictionary<string, string?> _values = new()
    {
        [TokenKeys.GitHub] = github,
        [TokenKeys.DevOps] = devops,
    };

    public string? Get(string key) => _values.GetValueOrDefault(key);
    public void Set(string key, string? value) => _values[key] = value;
}
