using System.Net;
using FluentAssertions;
using ResilienceDemo.Client;
using ResilienceDemo.Resilience;

namespace ResilienceDemo.Tests;

public class BulkheadTests
{
    [Fact]
    public async Task ExcessConcurrentCalls_AreRejectedInsteadOfUnboundedFanOut()
    {
        var options = new OutboundResilienceOptions
        {
            RetryMaxAttempts = 0,
            CircuitBreakerMinimumThroughput = 100,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(10),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(10),
            AttemptTimeout = TimeSpan.FromSeconds(2),
            BulkheadPermitLimit = 2,
            BulkheadQueueLimit = 1,
        };

        var handler = new FakeDownstreamHandler(FakeDownstreamHandler.Delayed(TimeSpan.FromMilliseconds(200), HttpStatusCode.OK));
        var harness = TestHarness.Build(options, handler);

        var tasks = Enumerable.Range(0, 6).Select(i => harness.Client.GetQuoteAsync(i, CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        var rejected = results.Count(r => r.Outcome == OutboundOutcome.BulkheadRejected);
        var succeeded = results.Count(r => r.Outcome == OutboundOutcome.Success);

        rejected.Should().Be(3);
        succeeded.Should().Be(3);
        harness.Metrics.BulkheadRejections.Should().Be(3);
    }
}
