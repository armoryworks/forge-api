using FluentAssertions;

using Forge.Api.Features.ComplianceForms;

namespace Forge.Tests.Handlers.ComplianceForms;

public class DocuSealWebhookEventTests
{
    [Fact]
    public void FormCompleted_FirstSigner_IsSubmitterEventOnTheNestedSubmission()
    {
        var e = DocuSealWebhookEvent.Parse("""
            {"event_type":"form.completed","timestamp":"2026-10-19T12:00:00Z",
             "data":{"id":9001,"role":"Employee","status":"completed","completed_at":"2026-10-19T11:59:00.123Z",
                     "submission":{"id":501,"status":"pending"}}}
            """)!;

        e.SubmissionId.Should().Be(501);
        e.SubmitterId.Should().Be(9001);
        e.IsSubmitterEvent.Should().BeTrue();
        e.AllSigned.Should().BeFalse();
        e.Status.Should().Be("completed");
        e.CompletedAt.Should().Be(new DateTimeOffset(2026, 10, 19, 11, 59, 0, 123, TimeSpan.Zero));
    }

    [Fact]
    public void FormCompleted_LastSigner_IsAllSigned()
    {
        var e = DocuSealWebhookEvent.Parse("""
            {"event_type":"form.completed","data":{"id":9002,"status":"completed","submission":{"id":501,"status":"completed"}}}
            """)!;

        e.SubmissionId.Should().Be(501);
        e.SubmitterId.Should().Be(9002);
        e.AllSigned.Should().BeTrue();
    }

    [Fact]
    public void SubmissionCompleted_UsesDataIdAsTheSubmission()
    {
        var e = DocuSealWebhookEvent.Parse("""
            {"event_type":"submission.completed","data":{"id":501,"status":"completed"}}
            """)!;

        e.SubmissionId.Should().Be(501);
        e.SubmitterId.Should().BeNull();
        e.IsSubmitterEvent.Should().BeFalse();
        e.AllSigned.Should().BeTrue();
    }

    [Fact]
    public void LegacySubmitterCompleted_ReadsSubmissionId()
    {
        var e = DocuSealWebhookEvent.Parse("""
            {"event_type":"submitter.completed","data":{"id":9001,"submission_id":501,"status":"completed"}}
            """)!;

        e.SubmissionId.Should().Be(501);
        e.SubmitterId.Should().Be(9001);
        e.IsSubmitterEvent.Should().BeTrue();
        e.AllSigned.Should().BeFalse();
    }

    [Fact]
    public void LegacyFormCompletedWithoutNestedSubmission_IsSubmissionLevel()
    {
        var e = DocuSealWebhookEvent.Parse("""
            {"event_type":"form.completed","data":{"id":501,"status":"completed"}}
            """)!;

        e.SubmissionId.Should().Be(501);
        e.IsSubmitterEvent.Should().BeFalse();
        e.AllSigned.Should().BeTrue();
    }

    [Fact]
    public void FormDeclined_MapsToDeclined()
    {
        var e = DocuSealWebhookEvent.Parse("""
            {"event_type":"form.declined","data":{"id":9001,"status":"declined","submission":{"id":501,"status":"declined"}}}
            """)!;

        e.SubmissionId.Should().Be(501);
        e.Status.Should().Be("declined");
    }

    [Theory]
    [InlineData("""{"event_type":"form.viewed","data":{"id":9001,"submission":{"id":501}}}""")]
    [InlineData("""{"event_type":"template.created","data":{"id":3}}""")]
    [InlineData("""{"data":{"id":3}}""")]
    [InlineData("""{"event_type":"form.completed","data":{"status":"completed"}}""")]
    [InlineData("not json")]
    public void IgnoredOrMalformed_ReturnsNull(string body)
        => DocuSealWebhookEvent.Parse(body).Should().BeNull();
}
