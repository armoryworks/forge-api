using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Converts raw pdf.js extraction data into ComplianceFormDefinition JSON.
/// Uses pattern detection to infer government form layout metadata
/// (step sections, amount lines, filing status, signature blocks, etc.).
/// </summary>
public interface IFormDefinitionParser
{
    /// <summary>
    /// Parse raw PDF extraction data into ComplianceFormDefinition JSON.
    /// </summary>
    /// <param name="rawResult">Raw text and annotation data from pdf.js.</param>
    /// <param name="formType">Form type identifier (e.g., "W4", "I9") for context.</param>
    /// <returns>JSON string matching the ComplianceFormDefinition schema.</returns>
    string Parse(PdfExtractionResult rawResult, string formType);
}
