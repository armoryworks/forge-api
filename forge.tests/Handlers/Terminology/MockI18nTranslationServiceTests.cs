using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Integrations;

namespace Forge.Tests.Handlers.Terminology;

public class MockI18nTranslationServiceTests
{
    [Fact]
    public async Task TranslateLabel_ReportsTranslationUnavailable()
    {
        var translator = new MockI18nTranslationService(NullLogger<MockI18nTranslationService>.Instance);

        (await translator.TranslateLabelAsync("the Work Order", "en", "es", CancellationToken.None)).Should().BeNull();
    }
}
