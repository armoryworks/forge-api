namespace Forge.Core.Entities;

/// <summary>One characteristic on a certificate of analysis, copied from a QC inspection result or summarised from the lot's SPC measurements.</summary>
public class CoaResult : BaseEntity
{
    public const string SourceInspection = "Inspection";
    public const string SourceSpc = "Spc";

    public int CertificateId { get; set; }
    public int SortOrder { get; set; }
    public string Characteristic { get; set; } = string.Empty;
    public string? Specification { get; set; }
    public decimal? SpecMin { get; set; }
    public decimal? SpecMax { get; set; }
    public decimal? SpecTarget { get; set; }
    public string? Result { get; set; }
    public string? Unit { get; set; }
    public string? Method { get; set; }
    public bool Pass { get; set; }

    /// <summary><see cref="SourceInspection"/> (a QC inspection result id) or <see cref="SourceSpc"/> (an SPC characteristic id).</summary>
    public string SourceType { get; set; } = SourceInspection;
    public int? SourceId { get; set; }

    public CertificateOfAnalysis Certificate { get; set; } = null!;
}
