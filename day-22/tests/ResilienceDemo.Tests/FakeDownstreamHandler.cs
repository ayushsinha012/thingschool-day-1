using System.Net;

namespace ResilienceDemo.Tests;

public sealed class FakeDownstreamHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
    private int _callCount;

    public FakeDownstreamHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public int CallCount => Volatile.Read(ref _callCount);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return await _handler(request, cancellationToken);
    }

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> AlwaysStatus(HttpStatusCode statusCode) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(statusCode));

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Delayed(TimeSpan delay, HttpStatusCode statusCode) =>
        async (_, ct) =>
        {
            await Task.Delay(delay, ct);
            return new HttpResponseMessage(statusCode);
        };
}
