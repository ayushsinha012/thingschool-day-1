using Microsoft.Extensions.Logging.Abstractions;
using ResilienceDemo.Client;
using ResilienceDemo.Resilience;

namespace ResilienceDemo.Tests;

public sealed record Harness(OutboundDependencyClient Client, ResilienceMetrics Metrics, FakeDownstreamHandler Handler);

public static class TestHarness
{
    public static Harness Build(OutboundResilienceOptions options, FakeDownstreamHandler handler)
    {
        var metrics = new ResilienceMetrics();
        var pipeline = OutboundResiliencePipelineFactory.Create(options, metrics, NullLogger.Instance);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://downstream.test") };
        var client = new OutboundDependencyClient(httpClient, pipeline, metrics, NullLogger<OutboundDependencyClient>.Instance);
        return new Harness(client, metrics, handler);
    }
}
