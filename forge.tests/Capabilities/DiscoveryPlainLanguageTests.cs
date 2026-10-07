using System.Text.RegularExpressions;

using FluentAssertions;

using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Discovery.GetQuestions;

namespace Forge.Tests.Capabilities;

public class DiscoveryPlainLanguageTests
{
    private static readonly Regex InternalCode = new(@"\b(Q-[A-Z]\d|PRESET-|CAP-)", RegexOptions.Compiled);

    public static TheoryData<string> SelfServeQuestionIds()
    {
        var data = new TheoryData<string>();
        foreach (var q in DiscoveryQuestionCatalog.ForMode(consultantMode: false)) data.Add(q.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(SelfServeQuestionIds))]
    public void SelfServeQuestion_showsNoInternalCodes(string questionId)
    {
        var q = DiscoveryQuestionCatalog.ForMode(consultantMode: false).Single(x => x.Id == questionId);

        InternalCode.IsMatch(q.Text).Should().BeFalse(q.Text);
        InternalCode.IsMatch(q.WhyAsking).Should().BeFalse(q.WhyAsking);
        foreach (var c in q.Choices ?? [])
            InternalCode.IsMatch(c.Label).Should().BeFalse(c.Label);
    }

    [Theory]
    [InlineData("Q-O3")]
    [InlineData("Q-A2")]
    [InlineData("Q-C2")]
    [InlineData("Q-C3")]
    [InlineData("Q-C4")]
    [InlineData("Q-V2")]
    [InlineData("Q-D3")]
    [InlineData("Q-D5")]
    public void RoutingDetail_isKeptAsAnInternalNote(string questionId)
    {
        var q = DiscoveryQuestionCatalog.ForMode(consultantMode: true).Single(x => x.Id == questionId);

        q.InternalNote.Should().NotBeNullOrWhiteSpace();
        q.WhyAsking.Should().NotBe(q.InternalNote);
    }

    [Fact]
    public async Task SelfServeQuestions_omitInternalNotes()
    {
        var result = await new GetDiscoveryQuestionsHandler()
            .Handle(new GetDiscoveryQuestionsQuery(ConsultantMode: false), CancellationToken.None);

        result.Questions.Should().OnlyContain(q => q.InternalNote == null);
    }

    [Fact]
    public async Task ConsultantQuestions_carryInternalNotes()
    {
        var result = await new GetDiscoveryQuestionsHandler()
            .Handle(new GetDiscoveryQuestionsQuery(ConsultantMode: true), CancellationToken.None);

        result.Questions.Single(q => q.Id == "Q-O3").InternalNote.Should().Contain("Q-S1");
    }

    [Fact]
    public void ServicesOnlyRecommendation_explainsItselfWithoutQuestionCodes()
    {
        var answers = new DiscoveryAnswerSet(
        [
            new DiscoveryAnswer("Q-S1", "products"),
            new DiscoveryAnswer("Q-O1", "3-10"),
            new DiscoveryAnswer("Q-O3", "services"),
        ]);

        var rec = DiscoveryRecommendationEngine.Recommend(answers);

        rec.PresetId.Should().Be("PRESET-08");
        InternalCode.IsMatch(rec.Rationale).Should().BeFalse(rec.Rationale);
        rec.Alternatives.Should().OnlyContain(a => !InternalCode.IsMatch(a.DistinguishingRationale));
    }
}
