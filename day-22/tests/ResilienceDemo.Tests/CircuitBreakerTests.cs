using System.Net;
using FluentAssertions;
using ResilienceDemo.Client;
using ResilienceDemo.Resilience;

namespace ResilienceDemo.Tests;

public class CircuitBreakerTests
{
    private static OutboundResilienceOptions FastOptions() => new()
    {
        RetryMaxAttempts = 0,
        CircuitBreakerFailureRatio = 0.5,
        CircuitBreakerMinimumThroughput = 4,
        CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(10),
        CircuitBreakerBreakDuration = TimeSpan.FromMilliseconds(500),
        AttemptTimeout = TimeSpan.FromSeconds(2),
        BulkheadPermitLimit = 10,
        BulkheadQueueLimit = 10,
    };

    [Fact]
    public async Task SustainedFailures_OpenTheCircuitAndFailFast()
    {
        var handler = new FakeDownstreamHandler(FakeDownstreamHandler.AlwaysStatus(HttpStatusCode.InternalServerError));
        var harness = TestHarness.Build(FastOptions(), handler);

        for (var i = 0; i < 4; i++)
        {
            await harness.Client.RecordEventAsync(CancellationToken.None);
        }

        harness.Metrics.CircuitState.Should().Be("Open");

        var callsBeforeShortCircuit = handler.CallCount;
        var result = await harness.Client.RecordEventAsync(CancellationToken.None);

        result.Outcome.Should().Be(OutboundOutcome.CircuitOpen);
        handler.CallCount.Should().Be(callsBeforeShortCircuit);
        harness.Metrics.CircuitShortCircuited.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AfterBreakDuration_HalfOpenProbeSucceedsAndClosesCircuit()
    {
        var failing = true;
        var handler = new FakeDownstreamHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(failing ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)));

        var harness = TestHarness.Build(FastOptions(), handler);

        for (var i = 0; i < 4; i++)
        {
            await harness.Client.RecordEventAsync(CancellationToken.None);
        }

        harness.Metrics.CircuitState.Should().Be("Open");

        failing = false;
        await Task.Delay(600);

        var probeResult = await harness.Client.RecordEventAsync(CancellationToken.None);

        probeResult.Outcome.Should().Be(OutboundOutcome.Success);
        harness.Metrics.CircuitState.Should().Be("Closed");
        harness.Metrics.Transitions.Should().Contain(t => t.To == "HalfOpen");
        harness.Metrics.Transitions.Should().Contain(t => t.To == "Closed");
    }
}
