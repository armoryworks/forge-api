using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Builds a hardcoded ComplianceFormDefinition JSON for a specific known form type.
/// Receives raw PDF extraction data to map annotation IDs to fields.
/// Falls back gracefully with synthetic IDs if annotations can't be matched.
/// </summary>
public interface IFormDefinitionBuilder
{
    ComplianceFormType FormType { get; }

    /// <summary>
    /// Monotonically-increasing version string for this builder's output format.
    /// Increment when the builder code changes so stale DB versions are auto-regenerated.
    /// </summary>
    string BuilderVersion { get; }

    /// <summary>
    /// Build the form definition JSON from raw PDF extraction data.
    /// </summary>
    string Build(PdfExtractionResult rawResult);
}
