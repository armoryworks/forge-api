using FluentAssertions;
using FluentValidation.TestHelper;
using MediatR;
using Moq;

using Forge.Api.Features.IoT;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.IoT;

public class MachineConnectionAdminTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly ITokenEncryptionService _encryption = new PrefixEncryption();
    private readonly WorkCenter _press;

    public MachineConnectionAdminTests()
    {
        _press = new WorkCenter { Name = "Press 4", Code = "P4" };
        _db.WorkCenters.Add(_press);
        _db.SaveChanges();
        _mediator
            .Setup(m => m.Send(It.IsAny<GetMachineConnectionsQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetMachineConnectionsQuery q, CancellationToken ct) => new GetMachineConnectionsHandler(_db).Handle(q, ct));
    }

    private static CreateMachineTagRequestModel Tag(string name, string node) =>
        new() { TagName = name, OpcNodeId = node, DataType = "Double", Unit = "C" };

    private Task<MachineConnectionResponseModel> CreateAsync(string? credentials = "opc-user:pw") =>
        new CreateMachineConnectionHandler(_db, _mediator.Object, _encryption).Handle(new CreateMachineConnectionCommand(new()
        {
            WorkCenterId = _press.Id,
            Name = "Press 4 PLC",
            OpcUaEndpoint = "opc.tcp://press4:4840",
            Credentials = credentials,
            Tags = [Tag("Barrel temp", "ns=2;s=Barrel"), Tag("Cycle count", "ns=2;s=Cycles")],
        }), CancellationToken.None);

    [Fact]
    public async Task Credentials_are_encrypted_and_only_reported_as_configured()
    {
        var created = await CreateAsync();

        created.CredentialsConfigured.Should().BeTrue();
        _db.MachineConnections.Single().EncryptedCredentials.Should().Be("enc:opc-user:pw");
    }

    [Fact]
    public async Task Editing_the_tag_map_keeps_history_for_tags_that_logged_data()
    {
        var created = await CreateAsync();
        var barrel = _db.MachineTags.Single(t => t.OpcNodeId == "ns=2;s=Barrel");
        var at = new DateTimeOffset(2026, 10, 27, 8, 30, 0, TimeSpan.Zero);
        _db.MachineDataPoints.Add(new MachineDataPoint { TagId = barrel.Id, WorkCenterId = _press.Id, Value = "212.5", Timestamp = at, Quality = MachineDataQuality.Good });
        _db.SaveChanges();

        var updated = await new UpdateMachineConnectionHandler(_db, _mediator.Object, _encryption).Handle(
            new UpdateMachineConnectionCommand(created.Id, new UpdateMachineConnectionRequestModel
            {
                Name = "Press 4 PLC",
                OpcUaEndpoint = "opc.tcp://press4:4840",
                PollIntervalMs = 500,
                Tags = [Tag("Mold temp", "ns=2;s=Mold")],
            }), CancellationToken.None);

        _db.MachineDataPoints.Should().ContainSingle(p => p.TagId == barrel.Id);
        _db.MachineTags.Single(t => t.Id == barrel.Id).IsActive.Should().BeFalse();
        _db.MachineTags.Should().NotContain(t => t.OpcNodeId == "ns=2;s=Cycles");
        updated.Tags.Where(t => t.IsActive).Select(t => t.OpcNodeId).Should().Equal("ns=2;s=Mold");
        updated.LastDataPointAt.Should().Be(at);
        _db.MachineConnections.Single().EncryptedCredentials.Should().Be("enc:opc-user:pw");
    }

    [Fact]
    public async Task Editing_a_kept_tag_updates_it_in_place()
    {
        var created = await CreateAsync(credentials: null);
        var barrelId = _db.MachineTags.Single(t => t.OpcNodeId == "ns=2;s=Barrel").Id;

        await new UpdateMachineConnectionHandler(_db, _mediator.Object, _encryption).Handle(
            new UpdateMachineConnectionCommand(created.Id, new UpdateMachineConnectionRequestModel
            {
                Name = "Press 4 PLC",
                OpcUaEndpoint = "opc.tcp://press4:4840",
                Credentials = "new:secret",
                Tags = [new CreateMachineTagRequestModel { TagName = "Barrel zone 1", OpcNodeId = "ns=2;s=Barrel", DataType = "Double", AlarmThresholdHigh = 260 }],
            }), CancellationToken.None);

        var tag = _db.MachineTags.Single(t => t.Id == barrelId);
        tag.TagName.Should().Be("Barrel zone 1");
        tag.AlarmThresholdHigh.Should().Be(260);
        _db.MachineConnections.Single().EncryptedCredentials.Should().Be("enc:new:secret");
    }

    [Fact]
    public void A_tag_map_with_a_repeated_node_id_is_refused()
    {
        var result = new CreateMachineConnectionValidator().TestValidate(new CreateMachineConnectionCommand(new()
        {
            WorkCenterId = 1,
            Name = "PLC",
            OpcUaEndpoint = "opc.tcp://plc:4840",
            Tags = [Tag("A", "ns=2;s=X"), Tag("B", "ns=2;s=X")],
        }));

        result.IsValid.Should().BeFalse();
    }

    private sealed class PrefixEncryption : ITokenEncryptionService
    {
        public string Encrypt(string plainText) => "enc:" + plainText;
        public string Decrypt(string cipherText) => cipherText[4..];
    }
}
