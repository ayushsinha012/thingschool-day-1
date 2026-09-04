using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;

namespace ResilienceDemo.Resilience;

public static class ResilienceContextKeys
{
    public static readonly ResiliencePropertyKey<bool> IsIdempotent = new("IsIdempotent");
}

public sealed class OutboundResilienceOptions
{
    public int RetryMaxAttempts { get; init; } = 3;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    public double CircuitBreakerFailureRatio { get; init; } = 0.5;
    public int CircuitBreakerMinimumThroughput { get; init; } = 4;
    public TimeSpan CircuitBreakerSamplingDuration { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan CircuitBreakerBreakDuration { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMilliseconds(800);

    public int BulkheadPermitLimit { get; init; } = 3;
    public int BulkheadQueueLimit { get; init; } = 2;
}

public static class OutboundResiliencePipelineFactory
{
    public static ResiliencePipeline<HttpResponseMessage> Create(
        OutboundResilienceOptions options,
        ResilienceMetrics metrics,
        ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        var bulkheadLimiter = new System.Threading.RateLimiting.ConcurrencyLimiter(new System.Threading.RateLimiting.ConcurrencyLimiterOptions
        {
            PermitLimit = options.BulkheadPermitLimit,
            QueueLimit = options.BulkheadQueueLimit,
            QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
        });

        builder.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = args => bulkheadLimiter.AcquireAsync(1, args.Context.CancellationToken),
            OnRejected = args =>
            {
                metrics.RecordBulkheadRejection();
                logger.LogWarning(
                    "bulkhead rejected permitLimit={PermitLimit} queueLimit={QueueLimit}",
                    options.BulkheadPermitLimit,
                    options.BulkheadQueueLimit);
                return ValueTask.CompletedTask;
            },
        });

        if (options.RetryMaxAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.RetryMaxAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = options.RetryBaseDelay,
                UseJitter = true,
                ShouldHandle = args =>
                {
                    if (!args.Context.Properties.TryGetValue(ResilienceContextKeys.IsIdempotent, out var idempotent) || !idempotent)
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (args.Outcome.Exception is BrokenCircuitException)
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (args.Outcome.Exception is TimeoutRejectedException || args.Outcome.Exception is HttpRequestException)
                    {
                        return ValueTask.FromResult(true);
                    }

                    var isTransientStatus = args.Outcome.Result is { } response &&
                        ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout);
                    return ValueTask.FromResult(isTransientStatus);
                },
                OnRetry = args =>
                {
                    metrics.RecordRetryAttempt();
                    logger.LogWarning(
                        "retry attempt={AttemptNumber} delay={DelayMs}ms reason={Reason}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds,
                        DescribeOutcome(args.Outcome));
                    return ValueTask.CompletedTask;
                },
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = options.CircuitBreakerFailureRatio,
            MinimumThroughput = options.CircuitBreakerMinimumThroughput,
            SamplingDuration = options.CircuitBreakerSamplingDuration,
            BreakDuration = options.CircuitBreakerBreakDuration,
            ShouldHandle = args =>
            {
                if (args.Outcome.Exception is TimeoutRejectedException || args.Outcome.Exception is HttpRequestException)
                {
                    return ValueTask.FromResult(true);
                }

                var isTransientStatus = args.Outcome.Result is { } response && (int)response.StatusCode >= 500;
                return ValueTask.FromResult(isTransientStatus);
            },
            OnOpened = args =>
            {
                metrics.RecordCircuitTransition("Closed", "Open", DescribeOutcome(args.Outcome));
                logger.LogError(
                    "circuit state=Open breakDuration={BreakDurationMs}ms lastReason={Reason}",
                    args.BreakDuration.TotalMilliseconds,
                    DescribeOutcome(args.Outcome));
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                metrics.RecordCircuitTransition("Open", "HalfOpen", "break duration elapsed, probing");
                logger.LogWarning("circuit state=HalfOpen reason=probing downstream after break duration");
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                metrics.RecordCircuitTransition("HalfOpen", "Closed", "probe succeeded");
                logger.LogInformation("circuit state=Closed reason=probe succeeded, downstream recovered");
                return ValueTask.CompletedTask;
            },
        });

        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = options.AttemptTimeout,
            OnTimeout = args =>
            {
                metrics.RecordTimeout();
                logger.LogWarning("timeout after={TimeoutMs}ms", args.Timeout.TotalMilliseconds);
                return ValueTask.CompletedTask;
            },
        });

        return builder.Build();
    }

    private static string DescribeOutcome(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is not null)
        {
            return outcome.Exception.GetType().Name;
        }

        return outcome.Result is { } response ? $"HTTP {(int)response.StatusCode}" : "unknown";
    }
}
