using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.I18nLabelOverrides;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.I18nLabelOverrides;

public class UpsertI18nLabelOverrideHandlerTests
{
    private readonly AppDbContext _db;
    private readonly Mock<II18nTranslationService> _translator = new();
    private readonly UpsertI18nLabelOverrideHandler _handler;

    public UpsertI18nLabelOverrideHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new UpsertI18nLabelOverrideHandler(_db, _translator.Object, new SystemClock());
    }

    private static UpsertI18nLabelOverrideCommand Command(
        string key = "customers.fields.name",
        string lang = "en",
        string value = "Client Name",
        bool translate = true)
        => new(new UpsertI18nLabelOverrideRequestModel(key, lang, value, translate));

    [Fact]
    public async Task Handle_NewKey_CreatesHumanOverride()
    {
        _translator.Setup(t => t.TranslateLabelAsync("Client Name", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Translated("Nombre del cliente"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        var primary = result.Overrides.Single(o => o.LanguageCode == "en");
        primary.Value.Should().Be("Client Name");
        primary.IsMachineTranslated.Should().BeFalse();
        primary.IsPendingTranslation.Should().BeFalse();
        result.TranslationsPending.Should().BeFalse();
        _db.I18nLabelOverrides.Count(o => o.Key == "customers.fields.name").Should().Be(2);
    }

    [Fact]
    public async Task Handle_FanOut_CreatesMachineTranslationForOtherLanguage()
    {
        _translator.Setup(t => t.TranslateLabelAsync("Client Name", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Translated("Nombre del cliente"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        var machine = result.Overrides.Single(o => o.LanguageCode == "es");
        machine.Value.Should().Be("Nombre del cliente");
        machine.IsMachineTranslated.Should().BeTrue();
        machine.IsPendingTranslation.Should().BeFalse();
        machine.SourceLanguageCode.Should().Be("en");
    }

    [Fact]
    public async Task Handle_FanOut_UsesConfiguredSupportedLanguages()
    {
        _db.SupportedLanguages.AddRange(
            new SupportedLanguage { Code = "en", Name = "English", NativeName = "English", IsActive = true },
            new SupportedLanguage { Code = "pt", Name = "Portuguese", NativeName = "Português", IsActive = true },
            new SupportedLanguage { Code = "zh", Name = "Chinese", NativeName = "中文", IsActive = false });
        await _db.SaveChangesAsync();
        _translator.Setup(t => t.TranslateLabelAsync(It.IsAny<string>(), "en", "pt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Translated("Nome do cliente"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Overrides.Select(o => o.LanguageCode).Should().BeEquivalentTo(["en", "pt"]);
        _translator.Verify(t => t.TranslateLabelAsync(It.IsAny<string>(), "en", "zh", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TranslationServiceUnavailable_SavesPrimaryAndFlagsSiblingPending()
    {
        _translator.Setup(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Unavailable);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.TranslationsPending.Should().BeTrue();
        var pending = result.Overrides.Single(o => o.LanguageCode == "es");
        pending.IsPendingTranslation.Should().BeTrue();
        pending.Value.Should().Be("Client Name"); // Source text kept as placeholder.
        result.Overrides.Single(o => o.LanguageCode == "en").IsPendingTranslation.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CannedAiReply_StoresNoSibling()
    {
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Canned assistant answer. This response is fixed for testing");
        var translator = new AiI18nTranslationService(ai.Object, NullLogger<AiI18nTranslationService>.Instance);
        var handler = new UpsertI18nLabelOverrideHandler(_db, translator, new SystemClock());

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.TranslationsPending.Should().BeFalse();
        result.Overrides.Select(o => o.LanguageCode).Should().BeEquivalentTo(["en"]);
        _db.I18nLabelOverrides.Any(o => o.LanguageCode == "es").Should().BeFalse();
        _db.ActivityLogs.Single(l => l.EntityType == "I18nLabelOverride" && l.Action == "created")
            .Description.Should().Contain("1 translation(s) discarded");
    }

    [Fact]
    public async Task Handle_RejectedReply_RemovesMachineRowFromEarlierValue()
    {
        _db.I18nLabelOverrides.Add(new I18nLabelOverride
        {
            Key = "customers.fields.name",
            LanguageCode = "es",
            Value = "Nombre anterior",
            IsMachineTranslated = true,
            SourceLanguageCode = "en",
        });
        await _db.SaveChangesAsync();
        _translator.Setup(t => t.TranslateLabelAsync("Client Name", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Rejected);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.TranslationsPending.Should().BeFalse();
        _db.I18nLabelOverrides.Any(o => o.LanguageCode == "es").Should().BeFalse();
        _db.I18nLabelOverrides.IgnoreQueryFilters().Single(o => o.LanguageCode == "es").DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ExistingHumanOverrideInOtherLanguage_IsNeverClobbered()
    {
        _db.I18nLabelOverrides.Add(new I18nLabelOverride
        {
            Key = "customers.fields.name",
            LanguageCode = "es",
            Value = "Cliente (manual)",
            IsMachineTranslated = false,
        });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        _translator.Verify(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _db.I18nLabelOverrides.Single(o => o.LanguageCode == "es").Value.Should().Be("Cliente (manual)");
        result.Overrides.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ExistingMachineTranslation_IsRetranslatedOnSourceEdit()
    {
        _db.I18nLabelOverrides.Add(new I18nLabelOverride
        {
            Key = "customers.fields.name",
            LanguageCode = "es",
            Value = "Vieja traducción",
            IsMachineTranslated = true,
            SourceLanguageCode = "en",
        });
        await _db.SaveChangesAsync();
        _translator.Setup(t => t.TranslateLabelAsync("Client Name", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Translated("Nombre del cliente"));

        await _handler.Handle(Command(), CancellationToken.None);

        var machine = _db.I18nLabelOverrides.Single(o => o.LanguageCode == "es");
        machine.Value.Should().Be("Nombre del cliente");
        machine.IsMachineTranslated.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UpdateOfMachineTranslatedRow_PromotesToHumanOverride()
    {
        _db.I18nLabelOverrides.Add(new I18nLabelOverride
        {
            Key = "customers.fields.name",
            LanguageCode = "es",
            Value = "Traducción automática",
            IsMachineTranslated = true,
            IsPendingTranslation = false,
            SourceLanguageCode = "en",
        });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(
            Command(lang: "es", value: "Nombre comercial", translate: false), CancellationToken.None);

        var row = result.Overrides.Single();
        row.LanguageCode.Should().Be("es");
        row.Value.Should().Be("Nombre comercial");
        row.IsMachineTranslated.Should().BeFalse();
        row.SourceLanguageCode.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TranslateToOtherLanguagesFalse_SkipsFanOut()
    {
        var result = await _handler.Handle(Command(translate: false), CancellationToken.None);

        result.Overrides.Should().HaveCount(1);
        _translator.Verify(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WritesActivityLogRow()
    {
        _translator.Setup(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Translated("Nombre del cliente"));

        await _handler.Handle(Command(), CancellationToken.None);

        // The DbContext also auto-writes generic "Created" rows; the handler's
        // curated kebab-case rollup row is the one under test here.
        var log = _db.ActivityLogs.Single(a => a.EntityType == "I18nLabelOverride" && a.Action == "created");
        log.Description.Should().Contain("customers.fields.name");
    }
}
