namespace ResilienceDemo.Downstream;

public sealed class DownstreamState
{
    private DownstreamMode _mode = DownstreamMode.Healthy;
    private int _delayMs;
    private long _requestCount;

    public DownstreamMode Mode => _mode;
    public int DelayMs => _delayMs;
    public long RequestCount => Interlocked.Read(ref _requestCount);

    public void Configure(DownstreamMode mode, int delayMs)
    {
        _mode = mode;
        _delayMs = delayMs;
    }

    public void CountRequest() => Interlocked.Increment(ref _requestCount);

    public void Reset()
    {
        _mode = DownstreamMode.Healthy;
        _delayMs = 0;
        Interlocked.Exchange(ref _requestCount, 0);
    }
}
