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
}
