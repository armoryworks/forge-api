using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Quotes;

/// <summary>
/// S4a — sales:auto_customer_po_enabled setting round-trip (mirrors the
/// AutoPo settings pattern): defaults false when absent, reads a persisted
/// value, and the update handler upserts + returns fresh state.
/// </summary>
public class QuoteSettingsHandlersTests
{
    private readonly Mock<ISystemSettingRepository> _settings = new();

    [Fact]
    public async Task Get_NoSettingRow_DefaultsToDisabled()
    {
        _settings.Setup(s => s.FindByKeyAsync("sales:auto_customer_po_enabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSetting?)null);
        var handler = new GetQuoteSettingsHandler(_settings.Object);

        var result = await handler.Handle(new GetQuoteSettingsQuery(), CancellationToken.None);

        result.AutoCustomerPoEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Get_PersistedTrue_ReturnsEnabled()
    {
        _settings.Setup(s => s.FindByKeyAsync("sales:auto_customer_po_enabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "sales:auto_customer_po_enabled", Value = "True" });
        var handler = new GetQuoteSettingsHandler(_settings.Object);

        var result = await handler.Handle(new GetQuoteSettingsQuery(), CancellationToken.None);

        result.AutoCustomerPoEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Update_UpsertsSettingAndReturnsFreshState()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetQuoteSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuoteSettingsResponseModel(true));
        var handler = new UpdateQuoteSettingsHandler(_settings.Object, mediator.Object);

        var result = await handler.Handle(
            new UpdateQuoteSettingsCommand(AutoCustomerPoEnabled: true), CancellationToken.None);

        _settings.Verify(s => s.UpsertAsync(
            "sales:auto_customer_po_enabled", "True", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _settings.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.AutoCustomerPoEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Update_NullField_PatchSemantics_DoesNotUpsert()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetQuoteSettingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuoteSettingsResponseModel(false));
        var handler = new UpdateQuoteSettingsHandler(_settings.Object, mediator.Object);

        await handler.Handle(new UpdateQuoteSettingsCommand(null), CancellationToken.None);

        _settings.Verify(s => s.UpsertAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
