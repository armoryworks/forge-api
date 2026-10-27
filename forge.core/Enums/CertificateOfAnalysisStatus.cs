namespace Forge.Core.Enums;

/// <summary>Lifecycle of a certificate of analysis (CAP-QC-COA). Results are frozen once issued; a wrong certificate is voided and a new one generated.</summary>
public enum CertificateOfAnalysisStatus
{
    Issued = 0,
    Voided = 1,
}
