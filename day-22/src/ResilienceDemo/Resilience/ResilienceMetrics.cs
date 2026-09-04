using System.Collections.Concurrent;

namespace ResilienceDemo.Resilience;

public sealed record TransitionEvent(DateTimeOffset Timestamp, string From, string To, string Reason);

public sealed class ResilienceMetrics
{
    private long _retryAttempts;
    private long _timeouts;
    private long _bulkheadRejections;
    private long _circuitShortCircuited;
    private long _successes;
    private long _failures;
    private string _circuitState = "Closed";
    private readonly ConcurrentQueue<TransitionEvent> _transitions = new();

    public long RetryAttempts => Interlocked.Read(ref _retryAttempts);
    public long Timeouts => Interlocked.Read(ref _timeouts);
    public long BulkheadRejections => Interlocked.Read(ref _bulkheadRejections);
    public long CircuitShortCircuited => Interlocked.Read(ref _circuitShortCircuited);
    public long Successes => Interlocked.Read(ref _successes);
    public long Failures => Interlocked.Read(ref _failures);
    public string CircuitState => _circuitState;
    public IReadOnlyCollection<TransitionEvent> Transitions => _transitions.ToArray();

    public void RecordRetryAttempt() => Interlocked.Increment(ref _retryAttempts);
    public void RecordTimeout() => Interlocked.Increment(ref _timeouts);
    public void RecordBulkheadRejection() => Interlocked.Increment(ref _bulkheadRejections);
    public void RecordCircuitShortCircuited() => Interlocked.Increment(ref _circuitShortCircuited);
    public void RecordSuccess() => Interlocked.Increment(ref _successes);
    public void RecordFailure() => Interlocked.Increment(ref _failures);

    public void RecordCircuitTransition(string from, string to, string reason)
    {
        _circuitState = to;
        _transitions.Enqueue(new TransitionEvent(DateTimeOffset.UtcNow, from, to, reason));
        while (_transitions.Count > 200 && _transitions.TryDequeue(out _))
        {
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _retryAttempts, 0);
        Interlocked.Exchange(ref _timeouts, 0);
        Interlocked.Exchange(ref _bulkheadRejections, 0);
        Interlocked.Exchange(ref _circuitShortCircuited, 0);
        Interlocked.Exchange(ref _successes, 0);
        Interlocked.Exchange(ref _failures, 0);
    }
}
