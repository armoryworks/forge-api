using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Terminology;

public class AiI18nTranslationServiceTests
{
    [Fact]
    public async Task TranslateLabel_AiTimesOut_ReturnsNullInsteadOfThrowing()
    {
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."));
        var service = new AiI18nTranslationService(ai.Object, NullLogger<AiI18nTranslationService>.Instance);

        var result = await service.TranslateLabelAsync("the Work Order", "en", "es", CancellationToken.None);

        result.Should().BeNull();
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
    [InlineData("Translate this software UI label", "Traduce esta software UI label")]
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
    public void IsPlausibleTranslation_AcceptsLabel(string source, string reply)
    {
        AiI18nTranslationService.IsPlausibleTranslation(source, reply).Should().BeTrue();
    }

    [Fact]
    public async Task TranslateLabel_CannedSentenceReply_ReturnsNull()
    {
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Canned assistant answer. This response is fixed for testing");
        var service = new AiI18nTranslationService(ai.Object, NullLogger<AiI18nTranslationService>.Instance);

        var result = await service.TranslateLabelAsync("the Work Order", "en", "es", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TranslateLabel_LoneTrailingPeriodSourceLacks_IsDropped()
    {
        var ai = new Mock<IAiService>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("La orden de trabajo.");
        var service = new AiI18nTranslationService(ai.Object, NullLogger<AiI18nTranslationService>.Instance);

        var result = await service.TranslateLabelAsync("the Work Order", "en", "es", CancellationToken.None);

        result.Should().Be("La orden de trabajo");
    }
}
