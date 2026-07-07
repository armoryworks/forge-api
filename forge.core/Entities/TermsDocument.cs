using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A versioned terms-and-conditions section. Scope Company applies to every
/// quote; Customer/Part sections are pulled in when the quote references them.
/// Summary is the author-controlled truncated blurb used inline in emails.
/// </summary>
public class TermsDocument : BaseAuditableEntity
{
    public TermsScope Scope { get; set; } = TermsScope.Company;
    public int? CustomerId { get; set; }
    public int? PartId { get; set; }
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(1000)]
    public string? Summary { get; set; }
    public string BodyMarkdown { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public int? SourceFileAttachmentId { get; set; }

    public Customer? Customer { get; set; }
    public Part? Part { get; set; }
    public FileAttachment? SourceFileAttachment { get; set; }
}
