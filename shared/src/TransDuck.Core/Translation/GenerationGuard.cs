namespace TransDuck.Core.Translation;

/// <summary>
/// Assigns monotonically increasing query IDs so stale stream callbacks are ignored.
/// </summary>
public sealed class GenerationGuard
{
    private long _currentGeneration;

    public long StartNewGeneration() => Interlocked.Increment(ref _currentGeneration);

    public void InvalidateCurrent() => Interlocked.Increment(ref _currentGeneration);

    public bool IsCurrent(long generation) =>
        Interlocked.Read(ref _currentGeneration) == generation;
}
