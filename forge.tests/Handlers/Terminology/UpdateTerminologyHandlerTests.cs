using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Features.Terminology;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Terminology;

public class UpdateTerminologyHandlerTests
{
    private readonly AppDbContext _db;
    private readonly Mock<IClientProxy> _allClients = new();
    private readonly UpdateTerminologyHandler _handler;

    public UpdateTerminologyHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(_allClients.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        _handler = new UpdateTerminologyHandler(new TerminologyRepository(_db), hub.Object);
    }

    private static UpdateTerminologyCommand Command(params (string Key, string Label)[] entries) =>
        new(entries.Select(e => new TerminologyEntryRequestModel(e.Key, e.Label)).ToList());

    [Fact]
    public async Task Handle_NewKey_AddsAdminEditedRow()
    {
        var result = await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        result.Should().ContainSingle(e => e.Key == "entity_job" && e.Label == "Work Order");
        var row = _db.TerminologyEntries.Single();
        row.IsAdminEdited.Should().BeTrue();
        row.SourcePresetId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PresetRowEdited_FlagsAdminEditAndClearsPreset()
    {
        _db.TerminologyEntries.Add(new TerminologyEntry { Key = "entity_job", Label = "Engagement", SourcePresetId = "PRESET-08" });
        await _db.SaveChangesAsync();

        await _handler.Handle(Command(("entity_job", "  Work Order ")), CancellationToken.None);

        var row = _db.TerminologyEntries.Single();
        row.Label.Should().Be("Work Order");
        row.IsAdminEdited.Should().BeTrue();
        row.SourcePresetId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BlankLabel_RemovesOverride()
    {
        _db.TerminologyEntries.AddRange(
            new TerminologyEntry { Key = "entity_job", Label = "Work Order" },
            new TerminologyEntry { Key = "entity_customer", Label = "Client" });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(Command(("entity_job", ""), ("label_jobs", " ")), CancellationToken.None);

        result.Should().ContainSingle().Which.Key.Should().Be("entity_customer");
        _db.TerminologyEntries.Select(e => e.Key).Should().Equal("entity_customer");
    }

    [Fact]
    public async Task Handle_Always_BroadcastsTerminologyChanged()
    {
        await _handler.Handle(Command(("entity_customer:es", "Socio")), CancellationToken.None);

        _allClients.Verify(c => c.SendCoreAsync("terminologyChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("entity_job", true)]
    [InlineData("label_jobs:es", true)]
    [InlineData("gender_job:es", true)]
    [InlineData("Entity_Job", false)]
    [InlineData("entity job", false)]
    [InlineData("entity_job:spanish", false)]
    public void Validator_KeyFormat(string key, bool valid)
    {
        var result = new UpdateTerminologyValidator().TestValidate(Command((key, "X")));

        if (valid)
            result.ShouldNotHaveAnyValidationErrors();
        else
            result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_BlankLabel_IsAllowed()
    {
        new UpdateTerminologyValidator().TestValidate(Command(("entity_job", ""))).ShouldNotHaveAnyValidationErrors();
    }
}
