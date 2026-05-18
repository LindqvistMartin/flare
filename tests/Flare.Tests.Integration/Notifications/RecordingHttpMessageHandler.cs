using System.Net;

namespace Flare.Tests.Integration.Notifications;

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly List<RecordedRequest> _requests = new();

    public HttpStatusCode ResponseStatus { get; init; } = HttpStatusCode.OK;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_lock)
                return _requests.ToArray();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read body now — once the response is returned, HttpClient may dispose request.Content.
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        lock (_lock)
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));

        return new HttpResponseMessage(ResponseStatus);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri? Uri, string Body);
