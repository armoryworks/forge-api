using FluentAssertions;
using Moq;

using Forge.Api.Features.I18nLabelOverrides;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.I18nLabelOverrides;

public class RetryPendingI18nTranslationsHandlerTests
{
    private readonly AppDbContext _db;
    private readonly Mock<II18nTranslationService> _translator = new();
    private readonly RetryPendingI18nTranslationsHandler _handler;

    public RetryPendingI18nTranslationsHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new RetryPendingI18nTranslationsHandler(_db, _translator.Object, new SystemClock());
    }

    private async Task SeedPendingPairAsync(string key = "customers.fields.name")
    {
        _db.I18nLabelOverrides.AddRange(
            new I18nLabelOverride { Key = key, LanguageCode = "en", Value = "Client Name" },
            new I18nLabelOverride
            {
                Key = key,
                LanguageCode = "es",
                Value = "Client Name",
                IsMachineTranslated = true,
                IsPendingTranslation = true,
                SourceLanguageCode = "en",
            });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_NoPendingRows_ReturnsZeroCounts()
    {
        var result = await _handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        result.TranslatedCount.Should().Be(0);
        result.StillPendingCount.Should().Be(0);
        _translator.Verify(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ServiceAvailable_TranslatesPendingRow()
    {
        await SeedPendingPairAsync();
        _translator.Setup(t => t.TranslateLabelAsync("Client Name", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Nombre del cliente");

        var result = await _handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        result.TranslatedCount.Should().Be(1);
        result.StillPendingCount.Should().Be(0);
        var row = _db.I18nLabelOverrides.Single(o => o.LanguageCode == "es");
        row.Value.Should().Be("Nombre del cliente");
        row.IsPendingTranslation.Should().BeFalse();
        row.IsMachineTranslated.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ServiceStillUnavailable_LeavesRowPending()
    {
        await SeedPendingPairAsync();
        _translator.Setup(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        result.TranslatedCount.Should().Be(0);
        result.StillPendingCount.Should().Be(1);
        _db.I18nLabelOverrides.Single(o => o.LanguageCode == "es").IsPendingTranslation.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SourceOverrideReverted_RemovesOrphanedPendingRow()
    {
        _db.I18nLabelOverrides.Add(new I18nLabelOverride
        {
            Key = "customers.fields.name",
            LanguageCode = "es",
            Value = "Client Name",
            IsMachineTranslated = true,
            IsPendingTranslation = true,
            SourceLanguageCode = "en",
        });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        result.TranslatedCount.Should().Be(0);
        result.StillPendingCount.Should().Be(0);
        _db.I18nLabelOverrides.Count().Should().Be(0); // Orphan soft-deleted.
    }

    [Fact]
    public async Task Handle_TranslatedRows_WriteActivityLogRollup()
    {
        await SeedPendingPairAsync();
        _translator.Setup(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Nombre del cliente");

        await _handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        // Filter to the handler's curated rollup row — the DbContext auto-writes "FieldChanged" rows too.
        _db.ActivityLogs.Single(a => a.EntityType == "I18nLabelOverride" && a.Action == "updated")
            .Description.Should().Contain("1 pending machine translation");
    }
}
