using System.Text.RegularExpressions;

using FluentAssertions;

using Forge.Api.Capabilities;

namespace Forge.Tests.Capabilities;

public class CapabilityCatalogPlainLanguageTests
{
    private static readonly Regex BuildNote = new(
        @"\bCAP-|\bPhase[ -]?\d|\bWU-\d|\b[A-Z]{2,}-\d{3}\b|§|mock-only|QuestPDF|Puppeteer|Default (OFF|ON)\b|CLAUDE\.md",
        RegexOptions.Compiled);

    public static TheoryData<string> CapabilityCodes()
    {
        var data = new TheoryData<string>();
        foreach (var def in CapabilityCatalog.All) data.Add(def.Code);
        return data;
    }

    [Theory]
    [MemberData(nameof(CapabilityCodes))]
    public void Description_carriesNoBuildNotesOrCodes(string code)
    {
        var def = CapabilityCatalog.All.Single(c => c.Code == code);

        BuildNote.IsMatch(def.Description).Should().BeFalse(def.Description);
    }

    [Theory]
    [InlineData("CAP-O2C-SETTLEMENT")]
    [InlineData("CAP-EXT-ECOMMERCE")]
    public void RetailDependency_isNamedInWords(string code)
    {
        CapabilityCatalog.All.Single(c => c.Code == code).Description
            .Should().Contain("Needs Retail orders turned on.");
    }
}
