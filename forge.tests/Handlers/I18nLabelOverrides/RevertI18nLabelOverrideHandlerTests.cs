using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.I18nLabelOverrides;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.I18nLabelOverrides;

public class RevertI18nLabelOverrideHandlerTests
{
    private readonly AppDbContext _db;
    private readonly RevertI18nLabelOverrideHandler _handler;

    public RevertI18nLabelOverrideHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new RevertI18nLabelOverrideHandler(_db, new SystemClock());
    }

    private async Task<I18nLabelOverride> SeedAsync(
        string lang, string value, bool machine = false, string? sourceLang = null)
    {
        var row = new I18nLabelOverride
        {
            Key = "customers.fields.name",
            LanguageCode = lang,
            Value = value,
            IsMachineTranslated = machine,
            SourceLanguageCode = sourceLang,
        };
        _db.I18nLabelOverrides.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task Handle_SoftDeletesOverride()
    {
        var row = await SeedAsync("en", "Client Name");

        await _handler.Handle(new RevertI18nLabelOverrideCommand(row.Id), CancellationToken.None);

        row.DeletedAt.Should().NotBeNull();
        (await _db.I18nLabelOverrides.CountAsync()).Should().Be(0); // Hidden by soft-delete query filter.
    }

    [Fact]
    public async Task Handle_HumanOverride_AlsoRemovesDerivedMachineTranslations()
    {
        var human = await SeedAsync("en", "Client Name");
        var derived = await SeedAsync("es", "Nombre del cliente", machine: true, sourceLang: "en");

        await _handler.Handle(new RevertI18nLabelOverrideCommand(human.Id), CancellationToken.None);

        derived.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_HumanOverride_KeepsHumanSiblings()
    {
        var human = await SeedAsync("en", "Client Name");
        var siblingHuman = await SeedAsync("es", "Cliente (manual)");

        await _handler.Handle(new RevertI18nLabelOverrideCommand(human.Id), CancellationToken.None);

        siblingHuman.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MachineTranslatedRow_RemovesOnlyItself()
    {
        var human = await SeedAsync("en", "Client Name");
        var derived = await SeedAsync("es", "Nombre del cliente", machine: true, sourceLang: "en");

        await _handler.Handle(new RevertI18nLabelOverrideCommand(derived.Id), CancellationToken.None);

        derived.DeletedAt.Should().NotBeNull();
        human.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WritesActivityLogRow()
    {
        var row = await SeedAsync("en", "Client Name");

        await _handler.Handle(new RevertI18nLabelOverrideCommand(row.Id), CancellationToken.None);

        // Filter to the handler's curated row — the DbContext auto-writes a generic "Deleted" row too.
        var log = _db.ActivityLogs.Single(a =>
            a.EntityType == "I18nLabelOverride" && a.EntityId == row.Id && a.Action == "deleted");
        log.Description.Should().Contain("Reverted");
    }

    [Fact]
    public async Task Handle_UnknownId_ThrowsKeyNotFound()
    {
        var act = () => _handler.Handle(new RevertI18nLabelOverrideCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
