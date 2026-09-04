using System.Net;
using FluentAssertions;
using ResilienceDemo.Client;
using ResilienceDemo.Resilience;

namespace ResilienceDemo.Tests;

public class TimeoutTests
{
    [Fact]
    public async Task SlowDownstream_ExceedsAttemptTimeoutAndIsReportedAsTimeout()
    {
        var options = new OutboundResilienceOptions
        {
            RetryMaxAttempts = 0,
            CircuitBreakerMinimumThroughput = 100,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(10),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(10),
            AttemptTimeout = TimeSpan.FromMilliseconds(100),
            BulkheadPermitLimit = 10,
            BulkheadQueueLimit = 10,
        };

        var handler = new FakeDownstreamHandler(FakeDownstreamHandler.Delayed(TimeSpan.FromMilliseconds(500), HttpStatusCode.OK));
        var harness = TestHarness.Build(options, handler);

        var result = await harness.Client.GetQuoteAsync(1, CancellationToken.None);

        result.Outcome.Should().Be(OutboundOutcome.Timeout);
        harness.Metrics.Timeouts.Should().Be(1);
    }
}
