using System.Net;
using System.Text;

namespace GridWatch.Tests.Support;

/// <summary>
/// Stands in for reports-public.ieso.ca. Serves registered report paths and returns 404 for anything
/// else, which is how the real site answers for an hour that has not been published yet.
/// </summary>
public sealed class FakeIesoHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];

    public HttpStatusCode? ForceStatus { get; set; }

    public FakeIesoHandler Serve(string path, string content)
    {
        _files[path.TrimStart('/')] = content;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.Replace("/public/", "", StringComparison.Ordinal).TrimStart('/');
        lock (Requests)
        {
            Requests.Add(path);
        }

        if (ForceStatus is { } status)
        {
            return Task.FromResult(new HttpResponseMessage(status));
        }

        return Task.FromResult(_files.TryGetValue(path, out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
