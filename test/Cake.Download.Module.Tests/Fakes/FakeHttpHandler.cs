using System.Net;
using System.Text;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = new(StringComparer.Ordinal);
    private readonly List<Uri> _requestedUrls = [];
    private readonly List<string> _userAgents = [];

    public IReadOnlyList<Uri> RequestedUrls
    {
        get
        {
            lock (_gate)
            {
                return [.. _requestedUrls];
            }
        }
    }

    public IReadOnlyList<string> UserAgents
    {
        get
        {
            lock (_gate)
            {
                return [.. _userAgents];
            }
        }
    }

    public static Func<HttpResponseMessage> Ok(byte[] content) =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    public static Func<HttpResponseMessage> Ok(string content) => Ok(Encoding.UTF8.GetBytes(content));

    public static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => new HttpResponseMessage(status);

    public static Func<HttpResponseMessage> RedirectTo(string location) => () =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    };

    public static Func<HttpResponseMessage> Stalling() =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) };

    public FakeHttpHandler Respond(string url, params Func<HttpResponseMessage>[] responses)
    {
        lock (_gate)
        {
            _responses[url] = new Queue<Func<HttpResponseMessage>>(responses);
        }

        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requestedUrls.Add(request.RequestUri!);
            _userAgents.Add(request.Headers.UserAgent.ToString());
            if (!_responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var queue))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var factory = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return factory();
        }
    }
}
