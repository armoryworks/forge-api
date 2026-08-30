using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.Devices;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Devices;

[Collection(PostgresCollection.Name)]
public sealed class SharedDeviceEnrollmentTests(PostgresFixture fixture)
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task Shared_token_enrolls_a_userless_device_with_a_hashed_device_credential()
    {
        await using var db = fixture.CreateContext();
        var clock = new FixedClock();
        var raw = OpaqueTokens.NewToken();
        db.DeviceEnrollmentTokens.Add(new DeviceEnrollmentToken
        {
            TokenHash = OpaqueTokens.Sha256Hex(raw), IsShared = true, IssuedByUserId = 1,
            CreatedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddMinutes(10),
        });
        await db.SaveChangesAsync();

        var handler = new EnrollSharedDeviceHandler(
            db, clock, Options.Create(new MobileOptions { InstanceName = "Armory Plastics" }),
            Mock.Of<ISystemAuditWriter>());
        var uuid = Guid.NewGuid().ToString();

        var result = await handler.Handle(
            new EnrollSharedDeviceCommand(raw, uuid, "Dock tablet", "android", null, null),
            CancellationToken.None);

        result.DeviceToken.Should().NotBeNullOrEmpty();
        result.InstanceName.Should().Be("Armory Plastics");

        var device = await db.UserDevices.SingleAsync(d => d.DeviceUuid == uuid);
        device.IsShared.Should().BeTrue();
        device.UserId.Should().BeNull();
        device.DeviceTokenHash.Should().Be(OpaqueTokens.Sha256Hex(result.DeviceToken));

        var again = () => handler.Handle(
            new EnrollSharedDeviceCommand(raw, Guid.NewGuid().ToString(), "Other", "ios", null, null),
            CancellationToken.None);
        await again.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Personal_token_is_refused_by_the_shared_path()
    {
        await using var db = fixture.CreateContext();
        var clock = new FixedClock();
        var raw = OpaqueTokens.NewToken();
        db.DeviceEnrollmentTokens.Add(new DeviceEnrollmentToken
        {
            TokenHash = OpaqueTokens.Sha256Hex(raw), IsShared = false, TargetUserId = 1,
            IssuedByUserId = 1, CreatedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddMinutes(10),
        });
        await db.SaveChangesAsync();

        var handler = new EnrollSharedDeviceHandler(
            db, clock, Options.Create(new MobileOptions()), Mock.Of<ISystemAuditWriter>());

        var act = () => handler.Handle(
            new EnrollSharedDeviceCommand(raw, Guid.NewGuid().ToString(), "Tablet", "android", null, null),
            CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
