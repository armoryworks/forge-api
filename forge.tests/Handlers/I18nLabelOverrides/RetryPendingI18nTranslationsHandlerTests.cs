using FluentAssertions;
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
        result.DiscardedCount.Should().Be(0);
        _translator.Verify(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ServiceAvailable_TranslatesPendingRow()
    {
        await SeedPendingPairAsync();
        _translator.Setup(t => t.TranslateLabelAsync("Client Name", "en", "es", It.IsAny<CancellationToken>()))
            .ReturnsAsync(I18nTranslationResult.Translated("Nombre del cliente"));

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
            .ReturnsAsync(I18nTranslationResult.Unavailable);

        var result = await _handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        result.TranslatedCount.Should().Be(0);
        result.StillPendingCount.Should().Be(1);
        _db.I18nLabelOverrides.Single(o => o.LanguageCode == "es").IsPendingTranslation.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CannedAiReply_DiscardsRowAndDoesNotAskAgain()
    {
        await SeedPendingPairAsync();
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Canned assistant answer. This response is fixed for testing");
        var translator = new AiI18nTranslationService(ai.Object, NullLogger<AiI18nTranslationService>.Instance);
        var handler = new RetryPendingI18nTranslationsHandler(_db, translator, new SystemClock());

        var first = await handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);
        var second = await handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        first.TranslatedCount.Should().Be(0);
        first.StillPendingCount.Should().Be(0);
        first.DiscardedCount.Should().Be(1);
        second.Should().Be(new RetryPendingI18nTranslationsResponseModel(0, 0, 0));
        _db.I18nLabelOverrides.Any(o => o.LanguageCode == "es").Should().BeFalse();
        ai.Verify(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Once);
        _db.ActivityLogs.Single(l => l.EntityType == "I18nLabelOverride" && l.Action == "deleted")
            .Description.Should().Contain("Discarded 1 machine translation");
    }

    [Fact]
    public async Task Handle_RejectedAndUnavailableTogether_OnlyUnavailableStaysPending()
    {
        await SeedPendingPairAsync("customers.fields.name");
        await SeedPendingPairAsync("orders.fields.number");
        var calls = 0;
        _translator.Setup(t => t.TranslateLabelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls == 1 ? I18nTranslationResult.Rejected : I18nTranslationResult.Unavailable);

        var result = await _handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        result.Should().Be(new RetryPendingI18nTranslationsResponseModel(0, 1, 1));
        _db.I18nLabelOverrides.Count(o => o.LanguageCode == "es" && o.IsPendingTranslation).Should().Be(1);
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
            .ReturnsAsync(I18nTranslationResult.Translated("Nombre del cliente"));

        await _handler.Handle(new RetryPendingI18nTranslationsCommand(), CancellationToken.None);

        // Filter to the handler's curated rollup row — the DbContext auto-writes "FieldChanged" rows too.
        _db.ActivityLogs.Single(a => a.EntityType == "I18nLabelOverride" && a.Action == "updated")
            .Description.Should().Contain("1 pending machine translation");
    }
}
