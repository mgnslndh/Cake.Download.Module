using System.Net;

namespace Cake.Download.Module.Http;

internal sealed class DownloadHttpException : Exception
{
    public DownloadHttpException(Uri url, HttpStatusCode statusCode, string message)
        : base(message)
    {
        Url = url;
        StatusCode = statusCode;
    }

    public Uri Url { get; }

    public HttpStatusCode StatusCode { get; }
}
