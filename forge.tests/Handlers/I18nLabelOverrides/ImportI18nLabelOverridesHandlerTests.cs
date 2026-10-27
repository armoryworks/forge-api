using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.I18nLabelOverrides;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.I18nLabelOverrides;

public class ImportI18nLabelOverridesHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly ImportI18nLabelOverridesHandler _handler;

    public ImportI18nLabelOverridesHandlerTests() => _handler = new ImportI18nLabelOverridesHandler(_db, new SystemClock());

    private Task<ImportI18nLabelOverridesResponseModel> Import(params ImportI18nLabelEntryModel[] entries) =>
        _handler.Handle(new ImportI18nLabelOverridesCommand(new ImportI18nLabelOverridesRequestModel("ES", [.. entries])), CancellationToken.None);

    private void Seed(string key, string value, bool machine = false)
    {
        _db.I18nLabelOverrides.Add(new I18nLabelOverride { Key = key, LanguageCode = "es", Value = value, IsMachineTranslated = machine });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Values_equal_to_the_shipped_catalog_are_not_stored()
    {
        var result = await Import(
            new ImportI18nLabelEntryModel("common.save", "Guardar", "Guardar"),
            new ImportI18nLabelEntryModel("common.cancel", " Cancelar ", "Cancelar"));

        result.Should().Be(new ImportI18nLabelOverridesResponseModel(0, 0, 0, 2));
        (await _db.I18nLabelOverrides.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Changed_values_become_human_overrides()
    {
        Seed("customers.title", "Clientes viejos", machine: true);

        var result = await Import(
            new ImportI18nLabelEntryModel("customers.title", "Cuentas", "Clientes"),
            new ImportI18nLabelEntryModel("jobs.title", "Órdenes", "Trabajos"));

        result.Created.Should().Be(1);
        result.Updated.Should().Be(1);
        var rows = await _db.I18nLabelOverrides.OrderBy(o => o.Key).ToListAsync();
        rows.Select(r => (r.Key, r.Value, r.LanguageCode, r.IsMachineTranslated))
            .Should().Equal(("customers.title", "Cuentas", "es", false), ("jobs.title", "Órdenes", "es", false));
        (await _db.ActivityLogs.CountAsync(a => a.EntityType == "I18nLabelOverride" && a.Description.StartsWith("Imported"))).Should().Be(2);
    }

    [Fact]
    public async Task Importing_the_shipped_value_over_an_override_reverts_it()
    {
        Seed("customers.title", "Cuentas");

        var result = await Import(new ImportI18nLabelEntryModel("customers.title", "Clientes", "Clientes"));

        result.Reverted.Should().Be(1);
        (await _db.I18nLabelOverrides.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Unchanged_overrides_and_blank_values_are_left_alone()
    {
        Seed("customers.title", "Cuentas");

        var result = await Import(
            new ImportI18nLabelEntryModel("customers.title", "Cuentas", "Clientes"),
            new ImportI18nLabelEntryModel("jobs.title", "  ", "Trabajos"));

        result.Should().Be(new ImportI18nLabelOverridesResponseModel(0, 0, 0, 2));
    }

    [Fact]
    public void A_key_may_appear_once()
    {
        var command = new ImportI18nLabelOverridesCommand(new ImportI18nLabelOverridesRequestModel("es",
        [
            new ImportI18nLabelEntryModel("a.b", "x", null),
            new ImportI18nLabelEntryModel("a.b ", "y", null),
        ]));

        new ImportI18nLabelOverridesValidator().Validate(command).IsValid.Should().BeFalse();
    }
}
