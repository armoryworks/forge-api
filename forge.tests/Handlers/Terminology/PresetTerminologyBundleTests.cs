using FluentAssertions;

using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Terminology;

namespace Forge.Tests.Handlers.Terminology;

public class PresetTerminologyBundleTests
{
    public static TheoryData<string> PresetsWithTerminology()
    {
        var data = new TheoryData<string>();
        foreach (var preset in PresetCatalog.All.Where(p => p.TerminologyBundle is not null))
            data.Add(preset.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(PresetsWithTerminology))]
    public void CatalogTerms_PairSingularWithMatchingPlural(string presetId)
    {
        var labels = PresetCatalog.All.Single(p => p.Id == presetId).TerminologyBundle!.Labels;

        foreach (var term in TerminologyTermCatalog.All)
        {
            var hasOne = labels.TryGetValue(term.SingularKey, out var one);
            var hasOther = labels.TryGetValue(term.PluralKey, out var other);
            hasOne.Should().Be(hasOther, $"{presetId} renames {term.Id} in only one of {term.SingularKey} / {term.PluralKey}");
            if (hasOne)
                other.Should().Be(EnglishPlural(one!), $"{presetId}'s {term.PluralKey} must be the plural of {term.SingularKey}");
        }
    }

    [Fact]
    public void PresetsWithTerminology_AreChecked()
    {
        PresetCatalog.All.Where(p => p.TerminologyBundle is not null).Select(p => p.Id)
            .Should().Contain(["PRESET-08", "PRESET-09"]);
    }

    private static string EnglishPlural(string singular)
    {
        if (singular.EndsWith('y') && singular.Length > 1 && !"aeiou".Contains(singular[^2]))
            return singular[..^1] + "ies";
        if (singular.EndsWith('s') || singular.EndsWith('x') || singular.EndsWith("ch") || singular.EndsWith("sh"))
            return singular + "es";
        return singular + "s";
    }
}
