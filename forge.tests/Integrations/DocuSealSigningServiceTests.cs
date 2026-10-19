using System.Net;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Integrations;

namespace Forge.Tests.Integrations;

public class DocuSealSigningServiceTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (DocuSealSigningService Service, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(DocuSealSettings.KeyApiUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("http://docuseal.test");
        return (new DocuSealSigningService(new HttpClient(handler), settings.Object, NullLogger<DocuSealSigningService>.Instance), handler);
    }

    [Fact]
    public async Task CreateSubmissionFromPdf_ReturnsTheSharedSubmissionIdNotASubmitterId()
    {
        var (service, _) = Create(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/templates/pdf" => Json("""{"id":77,"name":"I-9"}"""),
            "/api/submissions" => Json("""
                [{"id":9001,"submission_id":501,"embed_src":"http://docuseal.test/s/a"},
                 {"id":9002,"submission_id":501,"embed_src":"http://docuseal.test/s/b"}]
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var result = await service.CreateSubmissionFromPdfAsync("I-9", [1],
            [new SequentialSubmitter(1, "e@example.com", "E", "Employee"), new SequentialSubmitter(2, "r@example.com", "R", "Employer")],
            CancellationToken.None);

        result.SubmissionId.Should().Be(501);
        result.SubmittersByOrder[1].SubmitterId.Should().Be(9001);
        result.SubmittersByOrder[2].SubmitterId.Should().Be(9002);
    }

    [Fact]
    public async Task CreateSubmission_ReturnsTheSubmissionIdSoStatusPollingAsksForTheRightSubmission()
    {
        var (service, handler) = Create(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/submissions" => Json("""[{"id":9001,"submission_id":501,"embed_src":"http://docuseal.test/s/a"}]"""),
            "/api/submissions/501" => Json("""{"id":501,"status":"completed","completed_at":"2026-10-19T12:00:00Z"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var created = await service.CreateSubmissionAsync(77, "c@example.com", "C", CancellationToken.None);
        var status = await service.GetSubmissionStatusAsync(created.SubmissionId, CancellationToken.None);

        created.SubmissionId.Should().Be(501);
        created.SubmitterId.Should().Be(9001);
        status.Status.Should().Be("completed");
        handler.Requests.Last().RequestUri!.AbsolutePath.Should().Be("/api/submissions/501");
    }

    [Fact]
    public async Task CreateSubmission_ResponseWithoutSubmissionId_FallsBackToTheId()
    {
        var (service, _) = Create(_ => Json("""[{"id":501,"embed_src":"http://docuseal.test/s/a"}]"""));

        var created = await service.CreateSubmissionAsync(77, "c@example.com", "C", CancellationToken.None);

        created.SubmissionId.Should().Be(501);
    }
}
