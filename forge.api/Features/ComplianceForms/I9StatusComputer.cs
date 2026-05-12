using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.ComplianceForms;

/// <summary>
/// Computes I9ComplianceStatus from a ComplianceFormSubmission snapshot.
/// Called from admin user list and any endpoint that needs the derived status.
/// </summary>
public static class I9StatusComputer
{
    private static readonly TimeSpan ReverificationWarningWindow = TimeSpan.FromDays(90);

    /// <summary>
    /// Derive I-9 status. Pass <c>null</c> when no I-9 submission exists for the user.
    /// </summary>
    public static I9ComplianceStatus Compute(ComplianceFormSubmission? submission)
    {
        if (submission is null)
            return I9ComplianceStatus.NotStarted;

        var now = DateTimeOffset.UtcNow;

        // Fully complete + check for reverification
        if (submission.I9Section2SignedAt.HasValue)
        {
            if (submission.I9ReverificationDueAt.HasValue)
            {
                if (submission.I9ReverificationDueAt.Value <= now)
                    return I9ComplianceStatus.ReverificationOverdue;

                if (submission.I9ReverificationDueAt.Value - now <= ReverificationWarningWindow)
                    return I9ComplianceStatus.ReverificationDue;
            }

            return I9ComplianceStatus.Complete;
        }

        // Section 1 signed; Section 2 pending
        if (submission.I9Section1SignedAt.HasValue)
        {
            if (submission.I9Section2OverdueAt.HasValue && submission.I9Section2OverdueAt.Value <= now)
                return I9ComplianceStatus.Section2Overdue;

            return I9ComplianceStatus.Section2InProgress;
        }

        // Submission exists but employee hasn't signed Section 1 yet
        if (submission.DocuSealSubmissionId.HasValue)
            return I9ComplianceStatus.Section1InProgress;

        // Submission created but no DocuSeal link yet (data collection in progress)
        return I9ComplianceStatus.Section1InProgress;
    }
}
