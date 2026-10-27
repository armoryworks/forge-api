using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scanner;

public class ScanDeviceRegistryTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IHttpContextAccessor> _http = new();
    private readonly int _adminId;
    private readonly int _workerId;

    public ScanDeviceRegistryTests()
    {
        _adminId = AddUser("Ana", "Admin");
        _workerId = AddUser("Will", "Worker");
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _adminId.ToString())], "Test"));
        _http.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
    }

    private int AddUser(string first, string last, bool active = true)
    {
        var user = new ApplicationUser { UserName = $"{first}@t.test", Email = $"{first}@t.test", FirstName = first, LastName = last, IsActive = active };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user.Id;
    }

    private Task<ScanDeviceResponseModel> Pair(string deviceId, int? assignedUserId = null) =>
        new PairScanDeviceHandler(_db, _clock.Object, _http.Object)
            .Handle(new PairScanDeviceCommand(new ScanDeviceRequestModel($" {deviceId} ", " Dock gun ", assignedUserId)), CancellationToken.None);

    [Fact]
    public async Task Pairing_registers_the_device_under_the_admin_and_can_assign_it()
    {
        var device = await Pair("SN-100", _workerId);

        device.DeviceId.Should().Be("SN-100");
        device.DeviceName.Should().Be("Dock gun");
        device.AssignedUserId.Should().Be(_workerId);
        device.AssignedUserName.Should().Be("Worker, Will");
        device.PairedByName.Should().Be("Admin, Ana");
        (await _db.ActivityLogs.AnyAsync(a => a.EntityType == "UserScanDevice" && a.EntityId == device.Id && a.Action == "paired")).Should().BeTrue();
    }

    [Fact]
    public async Task A_device_id_can_be_paired_once()
    {
        await Pair("SN-200");

        var again = () => Pair("SN-200");

        await again.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Assigning_to_an_inactive_user_is_refused()
    {
        var inactive = AddUser("Ivy", "Gone", active: false);

        var pair = () => Pair("SN-300", inactive);

        await pair.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_reassigns_and_unassigns_and_logs_it()
    {
        var device = await Pair("SN-400");
        var handler = new UpdateScanDeviceHandler(_db);

        var assigned = await handler.Handle(new UpdateScanDeviceCommand(device.Id, new UpdateScanDeviceRequestModel("Dock gun", _workerId)), CancellationToken.None);
        var cleared = await handler.Handle(new UpdateScanDeviceCommand(device.Id, new UpdateScanDeviceRequestModel("Dock gun", null)), CancellationToken.None);

        assigned.AssignedUserName.Should().Be("Worker, Will");
        cleared.AssignedUserId.Should().BeNull();
        (await _db.ActivityLogs.CountAsync(a => a.EntityId == device.Id && a.Action == "assigned")).Should().Be(2);
    }

    [Fact]
    public async Task The_registry_lists_every_device_with_its_last_scan()
    {
        var first = await Pair("SN-500");
        await new PairScanDeviceHandler(_db, _clock.Object, _http.Object)
            .Handle(new PairScanDeviceCommand(new ScanDeviceRequestModel("SN-501", "Aisle gun")), CancellationToken.None);
        _db.ScanActionLogs.Add(new ScanActionLog { UserId = _workerId, DeviceId = "SN-500", Quantity = 1 });
        await _db.SaveChangesAsync();

        var list = await new GetScanDevicesHandler(_db).Handle(new GetScanDevicesQuery(), CancellationToken.None);

        list.Select(d => d.DeviceId).Should().Equal("SN-501", "SN-500");
        list.Single(d => d.Id == first.Id).LastSeenAt.Should().NotBeNull();
        list.Single(d => d.DeviceId == "SN-501").LastSeenAt.Should().BeNull();
    }

    [Fact]
    public async Task Unpairing_removes_any_device()
    {
        var device = await Pair("SN-600");

        await new UnpairScanDeviceHandler(_db).Handle(new UnpairScanDeviceCommand(device.Id), CancellationToken.None);

        (await _db.UserScanDevices.AnyAsync()).Should().BeFalse();
    }
}
