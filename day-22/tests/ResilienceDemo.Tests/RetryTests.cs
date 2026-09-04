using System.Net;
using FluentAssertions;
using ResilienceDemo.Client;
using ResilienceDemo.Resilience;

namespace ResilienceDemo.Tests;

public class RetryTests
{
    private static OutboundResilienceOptions FastOptions() => new()
    {
        RetryMaxAttempts = 3,
        RetryBaseDelay = TimeSpan.FromMilliseconds(10),
        CircuitBreakerMinimumThroughput = 100,
        CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(10),
        CircuitBreakerBreakDuration = TimeSpan.FromSeconds(10),
        AttemptTimeout = TimeSpan.FromSeconds(2),
        BulkheadPermitLimit = 10,
        BulkheadQueueLimit = 10,
    };

    [Fact]
    public async Task IdempotentGet_RetriesTransientFailuresAndEventuallySucceeds()
    {
        var attempt = 0;
        var handler = new FakeDownstreamHandler((_, _) =>
        {
            attempt++;
            var status = attempt < 3 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        });

        var harness = TestHarness.Build(FastOptions(), handler);

        var result = await harness.Client.GetQuoteAsync(1, CancellationToken.None);

        result.Outcome.Should().Be(OutboundOutcome.Success);
        handler.CallCount.Should().Be(3);
        harness.Metrics.RetryAttempts.Should().Be(2);
    }

    [Fact]
    public async Task NonIdempotentPost_DoesNotRetryOnTransientFailure()
    {
        var handler = new FakeDownstreamHandler(FakeDownstreamHandler.AlwaysStatus(HttpStatusCode.InternalServerError));
        var harness = TestHarness.Build(FastOptions(), handler);

        var result = await harness.Client.RecordEventAsync(CancellationToken.None);

        result.Outcome.Should().Be(OutboundOutcome.Failed);
        handler.CallCount.Should().Be(1);
        harness.Metrics.RetryAttempts.Should().Be(0);
    }
}
