using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Services;
using Forge.Core.Interfaces;

namespace Forge.Tests.Services;

public class ScanCollapseServiceTests
{
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void Same_device_code_and_action_inside_three_seconds_is_a_duplicate()
    {
        var clock = new MutableClock();
        var service = new ScanCollapseService(
            new MemoryCache(new MemoryCacheOptions()), clock, NullLogger<ScanCollapseService>.Instance);

        service.IsDuplicate("dev-1", "JOB-1042", "advance").Should().BeFalse();
        clock.UtcNow = clock.UtcNow.AddSeconds(1.5);
        service.IsDuplicate("dev-1", "JOB-1042", "advance").Should().BeTrue();
    }

    [Fact]
    public void Different_device_or_action_is_not_collapsed()
    {
        var service = new ScanCollapseService(
            new MemoryCache(new MemoryCacheOptions()), new MutableClock(), NullLogger<ScanCollapseService>.Instance);

        service.IsDuplicate("dev-1", "JOB-1042", "advance").Should().BeFalse();
        service.IsDuplicate("dev-2", "JOB-1042", "advance").Should().BeFalse();
        service.IsDuplicate("dev-1", "JOB-1042", "complete").Should().BeFalse();
    }

    [Fact]
    public void After_the_window_the_scan_is_fresh_again()
    {
        var clock = new MutableClock();
        var service = new ScanCollapseService(
            new MemoryCache(new MemoryCacheOptions()), clock, NullLogger<ScanCollapseService>.Instance);

        service.IsDuplicate("dev-1", "JOB-1042", "advance").Should().BeFalse();
        clock.UtcNow = clock.UtcNow.AddSeconds(4);
        service.IsDuplicate("dev-1", "JOB-1042", "advance").Should().BeFalse();
    }
}
