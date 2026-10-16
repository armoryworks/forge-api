using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.SignalR;
using Moq;

using Forge.Api.Capabilities.Discovery.Bundles;
using Forge.Api.Features.Presets.Apply.Layers;
using Forge.Api.Features.Terminology;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Terminology;

public class UpdateTerminologyHandlerTests
{
    private readonly AppDbContext _db;
    private readonly Mock<IClientProxy> _allClients = new();
    private readonly Mock<II18nTranslationService> _translator = new();
    private readonly UpdateTerminologyHandler _handler;

    public UpdateTerminologyHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(_allClients.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        _handler = new UpdateTerminologyHandler(_db, new TerminologyRepository(_db), _translator.Object, hub.Object);
    }

    private static UpdateTerminologyCommand Command(params (string Key, string Label)[] entries) =>
        new(entries.Select(e => new TerminologyEntryRequestModel(e.Key, e.Label)).ToList());

    private void Translates(string english, string? spanish) =>
        _translator.Setup(t => t.TranslateLabelAsync($"the {english}", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync(spanish);

    private TerminologyEntry Row(string key) => _db.TerminologyEntries.Single(e => e.Key == key);

    private async Task SeedAsync(params TerminologyEntry[] rows)
    {
        _db.TerminologyEntries.AddRange(rows);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_NewKey_AddsAdminEditedRow()
    {
        var result = await _handler.Handle(Command(("entity_customer", "Client")), CancellationToken.None);

        result.Entries.Should().Contain(e => e.Key == "entity_customer" && e.Label == "Client" && !e.IsMachineTranslated);
        var row = Row("entity_customer");
        row.IsAdminEdited.Should().BeTrue();
        row.SourcePresetId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PresetRowEdited_FlagsAdminEditAndClearsPreset()
    {
        await SeedAsync(new TerminologyEntry { Key = "entity_job", Label = "Engagement", SourcePresetId = "PRESET-08" });

        await _handler.Handle(Command(("entity_job", "  Work Order ")), CancellationToken.None);

        var row = Row("entity_job");
        row.Label.Should().Be("Work Order");
        row.IsAdminEdited.Should().BeTrue();
        row.SourcePresetId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BlankLabel_KeepsAdminClearedTombstoneAndReadsSkipIt()
    {
        await SeedAsync(
            new TerminologyEntry { Key = "entity_job", Label = "Engagement", SourcePresetId = "PRESET-08" },
            new TerminologyEntry { Key = "entity_customer", Label = "Client", IsAdminEdited = true });

        var result = await _handler.Handle(Command(("entity_job", ""), ("label_jobs", " ")), CancellationToken.None);

        result.Entries.Should().ContainSingle().Which.Key.Should().Be("entity_customer");
        var tombstone = Row("entity_job");
        tombstone.Label.Should().BeEmpty();
        tombstone.IsAdminEdited.Should().BeTrue();
        tombstone.SourcePresetId.Should().BeNull();
        _db.TerminologyEntries.Should().NotContain(e => e.Key == "label_jobs");
    }

    [Fact]
    public async Task Handle_ClearedTerm_StaysClearedWhenPresetIsReapplied()
    {
        await SeedAsync(new TerminologyEntry { Key = "entity_job", Label = "Engagement", SourcePresetId = "PRESET-08" });
        await _handler.Handle(Command(("entity_job", "")), CancellationToken.None);

        var bundle = new TerminologyBundle(new Dictionary<string, string> { ["entity_job"] = "Engagement", ["label_jobs"] = "Engagements" });
        var applied = await TerminologyBundleApplier.ApplyAsync(bundle, _db, "PRESET-08", CancellationToken.None);
        await _db.SaveChangesAsync();

        applied.SkippedCount.Should().Be(2);
        Row("entity_job").Label.Should().BeEmpty();
        var read = await new TerminologyRepository(_db).GetAllAsync(CancellationToken.None);
        read.Should().NotContain(e => e.Key == "entity_job" || e.Key == "label_jobs");
    }

    [Fact]
    public async Task Handle_ValueOverTombstone_RestoresTheRename()
    {
        await SeedAsync(new TerminologyEntry { Key = "entity_customer", Label = "", IsAdminEdited = true });

        var result = await _handler.Handle(Command(("entity_customer", "Client")), CancellationToken.None);

        result.Entries.Should().ContainSingle(e => e.Key == "entity_customer" && e.Label == "Client");
        _db.TerminologyEntries.Count(e => e.Key == "entity_customer").Should().Be(1);
    }

    [Fact]
    public async Task Handle_EnglishOnlyRename_StoresMachineTranslatedSpanishWithGender()
    {
        Translates("Work Order", "la orden de trabajo");

        var result = await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        result.UntranslatedTerms.Should().BeEmpty();
        result.Entries.Should().Contain(e => e.Key == "entity_job:es" && e.Label == "Orden de trabajo" && e.IsMachineTranslated);
        result.Entries.Should().Contain(e => e.Key == "gender_job:es" && e.Label == "f" && e.IsMachineTranslated);
        result.Entries.Should().NotContain(e => e.Key == "label_jobs:es");
        var row = Row("entity_job:es");
        row.IsAdminEdited.Should().BeTrue();
        row.SourcePresetId.Should().Be(TerminologyEntry.MachineTranslationSource);
    }

    [Fact]
    public async Task Handle_EnglishPlural_IsTranslatedToo()
    {
        Translates("Work Order", "La orden de trabajo.");
        Translates("Work Orders", "las órdenes de trabajo");

        var result = await _handler.Handle(Command(("entity_job", "Work Order"), ("label_jobs", "Work Orders")), CancellationToken.None);

        result.Entries.Should().Contain(e => e.Key == "entity_job:es" && e.Label == "Orden de trabajo");
        result.Entries.Should().Contain(e => e.Key == "label_jobs:es" && e.Label == "Órdenes de trabajo" && e.IsMachineTranslated);
    }

    [Fact]
    public async Task Handle_TranslationWithoutArticle_LeavesGenderToTheDefault()
    {
        Translates("Client", "cliente");

        var result = await _handler.Handle(Command(("entity_customer", "Client")), CancellationToken.None);

        result.Entries.Should().Contain(e => e.Key == "entity_customer:es" && e.Label == "Cliente");
        result.Entries.Should().NotContain(e => e.Key == "gender_customer:es");
    }

    [Fact]
    public async Task Handle_AdminEnteredSpanish_IsNeverOverwritten()
    {
        await SeedAsync(new TerminologyEntry { Key = "entity_job:es", Label = "Encargo", IsAdminEdited = true });
        Translates("Work Order", "la orden de trabajo");

        await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        Row("entity_job:es").Label.Should().Be("Encargo");
        _db.TerminologyEntries.Should().NotContain(e => e.Key == "gender_job:es");
        _translator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_AdminGenderOnly_StillTranslatesTheTextAndKeepsTheGender()
    {
        Translates("Work Order", "el orden de trabajo");

        var result = await _handler.Handle(Command(("entity_job", "Work Order"), ("gender_job:es", "f")), CancellationToken.None);

        result.Entries.Should().Contain(e => e.Key == "entity_job:es" && e.Label == "Orden de trabajo" && e.IsMachineTranslated);
        result.Entries.Should().Contain(e => e.Key == "gender_job:es" && e.Label == "f" && !e.IsMachineTranslated);
    }

    [Fact]
    public async Task Handle_AdminTypesSpanishOverMachineRows_DropsTheRestOfTheMachineSpanish()
    {
        Translates("Work Order", "la orden de trabajo");
        Translates("Work Orders", "las órdenes de trabajo");
        await _handler.Handle(Command(("entity_job", "Work Order"), ("label_jobs", "Work Orders")), CancellationToken.None);

        var result = await _handler.Handle(Command(("entity_job:es", "Encargo")), CancellationToken.None);

        result.Entries.Where(e => e.Key.EndsWith(":es") && e.Key.Contains("job")).Should()
            .ContainSingle().Which.Should().Be(new TerminologyEntryResponseModel("entity_job:es", "Encargo"));
    }

    [Fact]
    public async Task Handle_SpanishInTheSameSave_WinsOverTranslation()
    {
        Translates("Work Order", "la orden de trabajo");

        var result = await _handler.Handle(Command(("entity_job", "Work Order"), ("entity_job:es", "Pedido")), CancellationToken.None);

        result.Entries.Should().Contain(e => e.Key == "entity_job:es" && e.Label == "Pedido" && !e.IsMachineTranslated);
        _translator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_EnglishChangedAgain_RetranslatesMachineRows()
    {
        Translates("Work Order", "la orden de trabajo");
        Translates("Ticket", "el ticket");
        await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        var result = await _handler.Handle(Command(("entity_job", "Ticket")), CancellationToken.None);

        result.Entries.Should().Contain(e => e.Key == "entity_job:es" && e.Label == "Ticket" && e.IsMachineTranslated);
        result.Entries.Should().Contain(e => e.Key == "gender_job:es" && e.Label == "m");
    }

    [Fact]
    public async Task Handle_MachineValueEditedByAdmin_BecomesAdminSpanish()
    {
        Translates("Work Order", "la orden de trabajo");
        await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        var result = await _handler.Handle(Command(("entity_job:es", "Orden de producción")), CancellationToken.None);
        Translates("Ticket", "el ticket");
        await _handler.Handle(Command(("entity_job", "Ticket")), CancellationToken.None);

        result.Entries.Should().Contain(e => e.Key == "entity_job:es" && !e.IsMachineTranslated);
        Row("entity_job:es").Label.Should().Be("Orden de producción");
    }

    [Fact]
    public async Task Handle_TranslationUnavailable_ReportsTheTermAndDropsStaleMachineSpanish()
    {
        await SeedAsync(
            new TerminologyEntry { Key = "entity_job", Label = "Work Order", IsAdminEdited = true },
            new TerminologyEntry { Key = "entity_job:es", Label = "Orden de trabajo", IsAdminEdited = true, SourcePresetId = TerminologyEntry.MachineTranslationSource });
        Translates("Ticket", null);

        var result = await _handler.Handle(Command(("entity_job", "Ticket")), CancellationToken.None);

        result.UntranslatedTerms.Should().Equal("job");
        result.Entries.Should().Contain(e => e.Key == "entity_job" && e.Label == "Ticket");
        result.Entries.Should().NotContain(e => e.Key == "entity_job:es");
    }

    [Fact]
    public async Task Handle_EnglishCleared_ClearsItsMachineSpanish()
    {
        Translates("Work Order", "la orden de trabajo");
        await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        var result = await _handler.Handle(Command(("entity_job", "")), CancellationToken.None);

        result.Entries.Should().BeEmpty();
        Row("entity_job:es").Label.Should().BeEmpty();
        Row("entity_job:es").IsAdminEdited.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SpanishOnlyChange_DoesNotCallTheTranslator()
    {
        await _handler.Handle(Command(("entity_customer:es", "Socio")), CancellationToken.None);

        _translator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_UnchangedEnglish_DoesNotRetranslate()
    {
        await SeedAsync(
            new TerminologyEntry { Key = "entity_job", Label = "Work Order", IsAdminEdited = true },
            new TerminologyEntry { Key = "entity_job:es", Label = "Orden de trabajo", IsAdminEdited = true, SourcePresetId = TerminologyEntry.MachineTranslationSource });

        await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        _translator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_UnchangedEnglishWithoutSpanish_RetriesTheTranslation()
    {
        await SeedAsync(new TerminologyEntry { Key = "entity_job", Label = "Work Order", IsAdminEdited = true });
        Translates("Work Order", "la orden de trabajo");

        var result = await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        result.UntranslatedTerms.Should().BeEmpty();
        result.Entries.Should().Contain(e => e.Key == "entity_job:es" && e.Label == "Orden de trabajo" && e.IsMachineTranslated);
    }

    [Fact]
    public async Task Handle_UnchangedEnglishWithAdminClearedSpanish_DoesNotRetranslate()
    {
        await SeedAsync(
            new TerminologyEntry { Key = "entity_job", Label = "Work Order", IsAdminEdited = true },
            new TerminologyEntry { Key = "entity_job:es", Label = "", IsAdminEdited = true });

        await _handler.Handle(Command(("entity_job", "Work Order")), CancellationToken.None);

        _translator.VerifyNoOtherCalls();
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
