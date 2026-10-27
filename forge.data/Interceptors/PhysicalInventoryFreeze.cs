namespace Forge.Data.Interceptors;

/// <summary>
/// Lets the physical inventory's own posting and cancel write to the locations it froze. Everything else that
/// moves stock in a frozen scope is refused by <see cref="PhysicalInventoryFreezeInterceptor"/>.
/// </summary>
public static class PhysicalInventoryFreeze
{
    private static readonly AsyncLocal<int> Depth = new();

    /// <summary>True while a <see cref="Suspend"/> scope is open on this async flow.</summary>
    public static bool IsSuspended => Depth.Value > 0;

    /// <summary>Opens a scope in which saves skip the freeze check; dispose it to close the scope.</summary>
    public static IDisposable Suspend()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Depth.Value--;
        }
    }
}
