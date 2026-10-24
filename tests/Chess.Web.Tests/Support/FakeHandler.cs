using System.Net;
using System.Text;

namespace Chess.Web.Tests.Support;

/// <summary>Records each request and answers it with a canned response. Nothing leaves the process.</summary>
internal sealed class FakeHandler(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var recorded = new RecordedRequest(request.Method.Method, request.RequestUri!.PathAndQuery, body,
            string.Join(",", request.Headers.Accept));
        Requests.Add(recorded);
        return respond(recorded);
    }
}

internal sealed record RecordedRequest(string Method, string Target, string? Body, string Accept);

internal static class Canned
{
    public static HttpClient Client(FakeHandler handler) => new(handler) { BaseAddress = new Uri("http://api") };

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Sse(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/event-stream") };
}
