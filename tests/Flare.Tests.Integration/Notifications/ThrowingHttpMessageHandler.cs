namespace Flare.Tests.Integration.Notifications;

// Companion to RecordingHttpMessageHandler: forces every SendAsync to throw a specific
// exception so tests can assert on the channel's catch / wrap / scrub behavior without
// involving a real socket.
internal sealed class ThrowingHttpMessageHandler(Exception toThrow) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw toThrow;
}
