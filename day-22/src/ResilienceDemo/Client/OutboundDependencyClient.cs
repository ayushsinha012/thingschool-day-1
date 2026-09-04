using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using ResilienceDemo.Resilience;

namespace ResilienceDemo.Client;

public sealed class OutboundDependencyClient
{
    private readonly HttpClient _httpClient;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly ResilienceMetrics _metrics;
    private readonly ILogger<OutboundDependencyClient> _logger;

    public OutboundDependencyClient(
        HttpClient httpClient,
        ResiliencePipeline<HttpResponseMessage> pipeline,
        ResilienceMetrics metrics,
        ILogger<OutboundDependencyClient> logger)
    {
        _httpClient = httpClient;
        _pipeline = pipeline;
        _metrics = metrics;
        _logger = logger;
    }

    public Task<OutboundCallResult> GetQuoteAsync(int id, CancellationToken cancellationToken) =>
        ExecuteAsync(isIdempotent: true, ctx => new HttpRequestMessage(HttpMethod.Get, $"/downstream/quote/{id}"), cancellationToken);

    public Task<OutboundCallResult> RecordEventAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(isIdempotent: false, ctx => new HttpRequestMessage(HttpMethod.Post, "/downstream/events"), cancellationToken);

    private async Task<OutboundCallResult> ExecuteAsync(
        bool isIdempotent,
        Func<ResilienceContext, HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(ResilienceContextKeys.IsIdempotent, isIdempotent);

        try
        {
            var response = await _pipeline.ExecuteAsync(
                async ctx => await _httpClient.SendAsync(requestFactory(ctx), ctx.CancellationToken),
                context);

            if (response.IsSuccessStatusCode)
            {
                _metrics.RecordSuccess();
                _logger.LogInformation("call succeeded status={StatusCode}", (int)response.StatusCode);
                return new OutboundCallResult(OutboundOutcome.Success, (int)response.StatusCode, "ok");
            }

            _metrics.RecordFailure();
            _logger.LogWarning("call failed status={StatusCode}", (int)response.StatusCode);
            return new OutboundCallResult(OutboundOutcome.Failed, (int)response.StatusCode, $"downstream returned {(int)response.StatusCode}");
        }
        catch (BrokenCircuitException)
        {
            _metrics.RecordFailure();
            _metrics.RecordCircuitShortCircuited();
            _logger.LogWarning("call rejected reason=circuit-open");
            return new OutboundCallResult(OutboundOutcome.CircuitOpen, null, "circuit open, failed fast");
        }
        catch (TimeoutRejectedException)
        {
            _metrics.RecordFailure();
            _logger.LogWarning("call rejected reason=timeout");
            return new OutboundCallResult(OutboundOutcome.Timeout, null, "attempt timed out");
        }
        catch (RateLimiterRejectedException)
        {
            _metrics.RecordFailure();
            _logger.LogWarning("call rejected reason=bulkhead-full");
            return new OutboundCallResult(OutboundOutcome.BulkheadRejected, null, "bulkhead full, request rejected");
        }
        catch (HttpRequestException ex)
        {
            _metrics.RecordFailure();
            _logger.LogWarning(ex, "call failed reason=transport");
            return new OutboundCallResult(OutboundOutcome.Failed, null, ex.Message);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
