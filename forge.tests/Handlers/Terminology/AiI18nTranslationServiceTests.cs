using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Terminology;

public class AiI18nTranslationServiceTests
{
    [Fact]
    public async Task TranslateLabel_AiTimesOut_ReportsUnavailableInsteadOfThrowing()
    {
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."));
        var service = new AiI18nTranslationService(ai.Object, NullLogger<AiI18nTranslationService>.Instance);

        var result = await service.TranslateLabelAsync("the Work Order", "en", "es", CancellationToken.None);

        result.Should().Be(I18nTranslationResult.Unavailable);
    }

    [Fact]
    public async Task TranslateLabel_CallerCancels_StillThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cts.Token));
        var service = new AiI18nTranslationService(ai.Object, NullLogger<AiI18nTranslationService>.Instance);

        var act = () => service.TranslateLabelAsync("the Work Order", "en", "es", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("the work order", "Canned assistant answer. This response is fixed for testing")]
    [InlineData("Save", "[Mock AI] Generated response for: Translate this software UI label from English to Spanish")]
    [InlineData("Customer purchase order number", "Translate this software UI label from English to Spanish")]
    [InlineData("Work order", "Traducción: Orden de trabajo")]
    [InlineData("Work order", "Claro. Orden de trabajo")]
    [InlineData("Save", "Guardar\nThis is the Spanish word for save")]
    [InlineData("{{count}} orders", "órdenes")]
    [InlineData("Save", "Guardar Guardar Guardar Guardar Guardar Guardar Guardar Guardar Guardar")]
    public void IsPlausibleTranslation_RejectsReplyThatIsNotALabel(string source, string reply)
    {
        AiI18nTranslationService.IsPlausibleTranslation(source, reply).Should().BeFalse();
    }

    [Theory]
    [InlineData("the work order", "Orden de trabajo")]
    [InlineData("Save", "Guardar")]
    [InlineData("{{count}} orders", "{{count}} órdenes")]
    [InlineData("Traducción", "Translation")]
    [InlineData("Etiqueta", "Label")]
    [InlineData("Are you sure?", "¿Está seguro?")]
    [InlineData("PO Number", "N.º de orden de compra")]
    [InlineData("Loading…", "Cargando...")]
    [InlineData("Note: …", "Nota: …")]
    [InlineData("Order No.", "Núm. de pedido")]
    [InlineData("Max. quantity", "Max. Menge")]
    [InlineData("e.g. value", "z.B. Wert")]
    [InlineData("Order no.", "Bestell-Nr. Kunde")]
    [InlineData("Translate this software UI label", "Traduce esta software UI label")]
    [InlineData("Are you sure? This cannot be undone.", "¿Está seguro? Esto no se puede deshacer.")]
    public void IsPlausibleTranslation_AcceptsLabel(string source, string reply)
    {
        AiI18nTranslationService.IsPlausibleTranslation(source, reply).Should().BeTrue();
    }

    [Theory]
    [InlineData("PO Number", "N.º de orden de compra", "N.º de orden de compra")]
    [InlineData("Loading…", "Cargando...", "Cargando...")]
    [InlineData("Note: …", "Nota: …", "Nota: …")]
    public async Task TranslateLabel_ValidLabelWithPunctuation_IsTranslated(string source, string reply, string expected)
    {
        var service = ServiceReplying(reply);

        var result = await service.TranslateLabelAsync(source, "en", "es", CancellationToken.None);

        result.Should().Be(I18nTranslationResult.Translated(expected));
    }

    [Fact]
    public async Task TranslateLabel_CannedSentenceReply_IsRejected()
    {
        var service = ServiceReplying("Canned assistant answer. This response is fixed for testing");

        var result = await service.TranslateLabelAsync("the Work Order", "en", "es", CancellationToken.None);

        result.Should().Be(I18nTranslationResult.Rejected);
    }

    [Fact]
    public async Task TranslateLabel_EmptyReply_ReportsUnavailable()
    {
        var service = ServiceReplying("   ");

        var result = await service.TranslateLabelAsync("the Work Order", "en", "es", CancellationToken.None);

        result.Should().Be(I18nTranslationResult.Unavailable);
    }

    [Fact]
    public async Task TranslateLabel_LoneTrailingPeriodSourceLacks_IsDropped()
    {
        var service = ServiceReplying("La orden de trabajo.");

        var result = await service.TranslateLabelAsync("the Work Order", "en", "es", CancellationToken.None);

        result.Should().Be(I18nTranslationResult.Translated("La orden de trabajo"));
    }

    private static AiI18nTranslationService ServiceReplying(string reply)
    {
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reply);
        return new AiI18nTranslationService(ai.Object, NullLogger<AiI18nTranslationService>.Instance);
    }
}
