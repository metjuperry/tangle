namespace Tangle.Ai;

internal static class ProviderExecution
{
    // Adapters expose usage for the most recent call; keep it paired with its accounting write.
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}
