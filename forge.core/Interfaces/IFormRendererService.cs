namespace Forge.Core.Interfaces;

/// <summary>
/// Renders a ComplianceFormDefinition JSON to PNG screenshots (one per page).
/// Uses PuppeteerSharp to navigate to the Angular headless render route and screenshot the rendered form.
/// </summary>
public interface IFormRendererService
{
    /// <summary>
    /// Render all pages of a form definition to PNG images.
    /// </summary>
    /// <param name="formDefinitionJson">ComplianceFormDefinition JSON string</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of PNG byte arrays, one per page</returns>
    Task<List<byte[]>> RenderFormPagesAsync(string formDefinitionJson, CancellationToken ct);
}
