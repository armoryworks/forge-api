namespace Forge.Core.Models;

/// <summary>How a job-list field is filtered: which operators apply and how the value is read.</summary>
public enum JobListFieldKind
{
    Text,
    Number,
    Date,
    Enum,
    Boolean,
}
