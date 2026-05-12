using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

/// <summary>
/// Mock form renderer for development/testing. Returns a 1x1 white PNG placeholder.
/// </summary>
public class MockFormRendererService(ILogger<MockFormRendererService> logger) : IFormRendererService
{
    private static readonly byte[] WhitePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    public Task<List<byte[]>> RenderFormPagesAsync(string formDefinitionJson, CancellationToken ct)
    {
        logger.LogInformation("[Mock] Rendering form definition ({Length} chars)", formDefinitionJson.Length);
        return Task.FromResult(new List<byte[]> { WhitePng });
    }
}
