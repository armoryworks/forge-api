using FluentAssertions;

using Forge.Api.Capabilities;
using Forge.Api.Features.Capabilities.Descriptor;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Capabilities;

public class CapabilityDescriptorOnboardingTests
{
    private readonly Data.Context.AppDbContext _db = TestDbContextFactory.Create();

    private Task<CapabilityDescriptorResponseModel> Describe()
        => new GetCapabilityDescriptorHandler(_db, StubCapabilitySnapshotProvider.Off)
            .Handle(new GetCapabilityDescriptorQuery(), CancellationToken.None);

    [Fact]
    public async Task FreshInstall_IsNotOnboarded()
    {
        (await Describe()).OnboardingComplete.Should().BeFalse();
    }

    [Fact]
    public async Task ManualCapabilityToggle_DoesNotCountAsOnboarding()
    {
        _db.AuditLogEntries.Add(new AuditLogEntry { Action = CapabilityAuditEvents.Enabled, EntityType = CapabilityAuditEvents.EntityType, UserId = 1 });
        await _db.SaveChangesAsync();

        (await Describe()).OnboardingComplete.Should().BeFalse();
    }

    [Fact]
    public async Task AppliedPreset_MarksOnboardingComplete()
    {
        _db.AuditLogEntries.Add(new AuditLogEntry { Action = CapabilityAuditEvents.PresetApplied, EntityType = CapabilityAuditEvents.EntityType, UserId = 1 });
        await _db.SaveChangesAsync();

        (await Describe()).OnboardingComplete.Should().BeTrue();
    }

    [Fact]
    public async Task RecordedDiscoveryRun_MarksOnboardingComplete()
    {
        _db.DiscoveryRuns.Add(new DiscoveryRun
        {
            RunByUserId = 1,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            RecommendedPresetId = "PRESET-A",
            AppliedPresetId = "PRESET-A",
        });
        await _db.SaveChangesAsync();

        (await Describe()).OnboardingComplete.Should().BeTrue();
    }
}
