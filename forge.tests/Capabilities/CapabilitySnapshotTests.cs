using Forge.Api.Capabilities;

namespace Forge.Tests.Capabilities;

/// <summary>
/// Phase 4 Phase-A — unit tests for the in-memory snapshot.
/// </summary>
public class CapabilitySnapshotTests
{
    [Fact]
    public void IsEnabled_Returns_True_For_Enabled_Code()
    {
        var snap = new CapabilitySnapshot(
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["CAP-MD-CUSTOMERS"] = true,
                ["CAP-INV-LOTS"] = false,
            },
            DateTimeOffset.UtcNow);

        Assert.True(snap.IsEnabled("CAP-MD-CUSTOMERS"));
        Assert.False(snap.IsEnabled("CAP-INV-LOTS"));
        Assert.False(snap.IsEnabled("CAP-DOES-NOT-EXIST"));
    }

    [Fact]
    public void Empty_Snapshot_Returns_False_For_All_Lookups()
    {
        var snap = CapabilitySnapshot.Empty;
        Assert.False(snap.IsEnabled("CAP-IDEN-USERS"));
        Assert.Empty(snap.EnabledByCode);
    }
}
