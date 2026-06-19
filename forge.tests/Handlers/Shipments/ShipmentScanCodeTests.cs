using FluentAssertions;

using Forge.Api.Features.Shipments;
using Forge.Api.Services;
using Forge.Core.Entities;

namespace Forge.Tests.Handlers.Shipments;

public class ShipmentScanCodeTests
{
    private static List<ShipmentLine> Lines(params (int soLineId, decimal qty)[] lines)
        => lines.Select(l => new ShipmentLine { SalesOrderLineId = l.soLineId, Quantity = l.qty }).ToList();

    [Fact]
    public void Compute_is_deterministic_and_versioned()
    {
        var a = ShipmentScanCode.Compute("SH-00001", Lines((10, 5), (11, 2)));
        var b = ShipmentScanCode.Compute("SH-00001", Lines((11, 2), (10, 5))); // order independent
        a.Should().Be(b);
        a.Should().StartWith("v1.SH-00001.");
    }

    [Fact]
    public void Compute_changes_when_coverage_changes()
    {
        var five = ShipmentScanCode.Compute("SH-00001", Lines((10, 5)));
        var six = ShipmentScanCode.Compute("SH-00001", Lines((10, 6)));
        five.Should().NotBe(six, "a different shipped quantity is different coverage");
    }

    [Fact]
    public void Scoped_code_is_distinct_from_master_even_for_identical_coverage()
    {
        var lines = Lines((10, 5));
        var master = ShipmentScanCode.Compute("SH-00001", lines);
        var scoped = ShipmentScanCode.ComputeForScope("SH-00001", "S42", lines);
        scoped.Should().StartWith("v1.SH-00001.S42.");
        scoped.Should().NotBe(master, "the scope segment keeps a per-SO QR distinct from the master");
    }

    [Fact]
    public void QrCodeRenderer_emits_a_valid_png()
    {
        var png = QrCodeRenderer.Png("v1.SH-00001.abc123");
        png.Should().NotBeEmpty();
        // PNG signature: 0x89 'P' 'N' 'G'.
        png.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
    }
}
