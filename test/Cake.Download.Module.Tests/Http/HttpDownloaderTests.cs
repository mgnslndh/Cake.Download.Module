using System.Net;
using System.Net.Http.Headers;
using Cake.Core;
using Cake.Download.Module.Http;
using Cake.Download.Module.Tests.Fakes;
using Cake.Testing;

namespace Cake.Download.Module.Tests.Http;

public sealed class HttpDownloaderTests : IDisposable
{
    private const string Url = "https://example.com/tool";
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    private readonly TestDirectory _directory = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly FakeLog _log = new();
    private readonly List<TimeSpan> _delays = [];

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Download_Writes_The_File_And_Computes_Its_Sha256()
    {
        _handler.Respond(Url, FakeHttpHandler.Ok("hello"));

        var result = Download();

        Assert.Equal(HelloSha256, result.Sha256);
        Assert.Equal(5, result.Length);
        Assert.Equal("hello", File.ReadAllText(result.Path));
    }

    [Fact]
    public void Download_Follows_Redirects_And_Sends_A_User_Agent()
    {
        _handler
            .Respond(Url, FakeHttpHandler.RedirectTo("https://objects.example.com/signed?sig=1"))
            .Respond("https://objects.example.com/signed?sig=1", FakeHttpHandler.RedirectTo("/final"))
            .Respond("https://objects.example.com/final", FakeHttpHandler.Ok("hello"));

        var result = Download();

        Assert.Equal(HelloSha256, result.Sha256);
        Assert.Equal(3, _handler.RequestedUrls.Count);
        Assert.All(_handler.UserAgents, agent => Assert.StartsWith("Cake.Download.Module/", agent));
    }

    [Fact]
    public void Download_Refuses_A_Redirect_To_Http()
    {
        _handler.Respond(Url, FakeHttpHandler.RedirectTo("http://example.com/tool"));

        var exception = Assert.Throws<CakeException>(() => Download());

        Assert.Equal("https://example.com/tool redirected to http://example.com/tool, which is not https. Only https downloads are supported.", exception.Message);
    }

    [Fact]
    public void Download_Refuses_Too_Many_Redirects()
    {
        _handler
            .Respond(Url, FakeHttpHandler.RedirectTo("https://example.com/a"))
            .Respond("https://example.com/a", FakeHttpHandler.RedirectTo("https://example.com/b"))
            .Respond("https://example.com/b", FakeHttpHandler.RedirectTo("https://example.com/c"));

        var exception = Assert.Throws<CakeException>(() => Download(new DownloadOptions { MaxRedirects = 2 }));

        Assert.Equal("https://example.com/tool redirected more than 2 times.", exception.Message);
    }

    [Fact]
    public void Download_Does_Not_Retry_A_404()
    {
        var exception = Assert.Throws<DownloadHttpException>(() => Download());

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("Downloading https://example.com/tool failed: HTTP 404 (Not Found).", exception.Message);
        Assert.Single(_handler.RequestedUrls);
    }

    [Fact]
    public void Download_Retries_A_Server_Error_With_Backoff()
    {
        _handler.Respond(Url, FakeHttpHandler.Status(HttpStatusCode.InternalServerError), FakeHttpHandler.Ok("hello"));

        Assert.Equal(HelloSha256, Download().Sha256);
        Assert.Equal(2, _handler.RequestedUrls.Count);
        Assert.Equal([TimeSpan.FromSeconds(1)], _delays);
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(120, 30)]
    public void Download_Honours_Retry_After_Up_To_The_Cap(int retryAfterSeconds, int expectedSeconds)
    {
        _handler.Respond(
            Url,
            () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
                return response;
            },
            FakeHttpHandler.Ok("hello"));

        Download();

        Assert.Equal([TimeSpan.FromSeconds(expectedSeconds)], _delays);
    }

    [Fact]
    public void Download_Gives_Up_After_The_Last_Attempt()
    {
        _handler.Respond(Url, FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable));

        var exception = Assert.Throws<CakeException>(() => Download());

        Assert.Equal("Downloading https://example.com/tool failed after 3 attempts: HTTP 503 (Service Unavailable).", exception.Message);
        Assert.Equal(3, _handler.RequestedUrls.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], _delays);
    }

    [Fact]
    public void Download_Retries_A_Stalled_Body()
    {
        _handler.Respond(Url, FakeHttpHandler.Stalling(), FakeHttpHandler.Ok("hello"));

        var result = Download(new DownloadOptions { StallTimeout = TimeSpan.FromMilliseconds(200) });

        Assert.Equal(HelloSha256, result.Sha256);
        Assert.Contains(_log.Entries, entry => entry.Message.Contains("No data received from https://example.com/tool for 0.2 s", StringComparison.Ordinal));
    }

    [Fact]
    public void Download_Enforces_The_Size_Limit_Without_Retrying()
    {
        _handler.Respond(Url, FakeHttpHandler.Ok("hello world"));

        var exception = Assert.Throws<CakeException>(() => Download(maxBytes: 5));

        Assert.Equal("https://example.com/tool is larger than the 5 bytes allowed.", exception.Message);
        Assert.Single(_handler.RequestedUrls);
    }

    private DownloadResult Download(DownloadOptions? options = null, long? maxBytes = null)
    {
        var effective = (options ?? new DownloadOptions()) with
        {
            Delay = (delay, _) =>
            {
                _delays.Add(delay);
                return Task.CompletedTask;
            },
        };

        return new HttpDownloader(_handler, _log, effective).Download(new Uri(Url), _directory.Combine("download"), maxBytes);
    }
}
