using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockDocumentSigningService : IDocumentSigningService
{
    private readonly ILogger<MockDocumentSigningService> _logger;
    private int _nextTemplateId = 1000;
    private int _nextSubmissionId = 5000;

    public MockDocumentSigningService(ILogger<MockDocumentSigningService> logger)
    {
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockDocuSeal] IsAvailable — returning true");
        return Task.FromResult(true);
    }

    public Task<int> CreateTemplateFromPdfAsync(string name, byte[] pdfBytes, CancellationToken ct)
    {
        var templateId = Interlocked.Increment(ref _nextTemplateId);
        _logger.LogInformation("[MockDocuSeal] CreateTemplate: {Name} ({Size} bytes) → template {Id}",
            name, pdfBytes.Length, templateId);
        return Task.FromResult(templateId);
    }

    public Task<DocumentSigningSubmission> CreateSubmissionAsync(int templateId, string signerEmail, string signerName, CancellationToken ct)
    {
        var submissionId = Interlocked.Increment(ref _nextSubmissionId);
        var submitUrl = $"http://localhost:3000/s/{submissionId}";
        _logger.LogInformation("[MockDocuSeal] CreateSubmission: template {TemplateId} for {Email} → submission {Id}",
            templateId, signerEmail, submissionId);
        return Task.FromResult(new DocumentSigningSubmission(submissionId, submitUrl));
    }

    public Task<DocumentSigningMultiSubmission> CreateSubmissionFromPdfAsync(
        string templateName,
        byte[] pdfBytes,
        IReadOnlyList<SequentialSubmitter> submitters,
        CancellationToken ct)
    {
        var templateId = Interlocked.Increment(ref _nextTemplateId);
        var byOrder = new Dictionary<int, SubmitterResult>();
        foreach (var submitter in submitters.OrderBy(s => s.Order))
        {
            var submitterId = Interlocked.Increment(ref _nextSubmissionId);
            byOrder[submitter.Order] = new SubmitterResult(submitterId, $"http://localhost:3000/s/{submitterId}");
        }

        _logger.LogInformation(
            "[MockDocuSeal] CreateSubmissionFromPdf: '{Name}' ({Size} bytes), {Count} submitters → template {TemplateId}",
            templateName, pdfBytes.Length, submitters.Count, templateId);

        return Task.FromResult(new DocumentSigningMultiSubmission(templateId, byOrder));
    }

    public Task<byte[]> GetSignedPdfAsync(int submissionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockDocuSeal] GetSignedPdf: submission {Id}", submissionId);
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<DocumentSigningSubmissionStatus> GetSubmissionStatusAsync(int submissionId, CancellationToken ct)
    {
        _logger.LogInformation("[MockDocuSeal] GetSubmissionStatus: submission {Id}", submissionId);
        return Task.FromResult(new DocumentSigningSubmissionStatus("completed", DateTimeOffset.UtcNow));
    }

    public Task DeleteTemplateAsync(int templateId, CancellationToken ct)
    {
        _logger.LogInformation("[MockDocuSeal] DeleteTemplate: {Id}", templateId);
        return Task.CompletedTask;
    }
}
