using FluentAssertions;

using Forge.Core.Settings;

namespace Forge.Tests.Integrations;

public class IntegrationModeDescriptionTests
{
    public static TheoryData<string> ModeKeys => new() { SmtpSettings.KeyMode, AiSettings.KeyMode };

    [Theory]
    [MemberData(nameof(ModeKeys))]
    public void ModeDescription_ExplainsEveryChoice(string key)
    {
        var descriptor = SmtpSettings.Descriptors.Concat(AiSettings.Descriptors).Single(d => d.Key == key);

        foreach (var choice in descriptor.Choices!)
            descriptor.Description.Should().Contain(choice.Value);
    }
}
