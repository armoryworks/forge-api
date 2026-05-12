using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IWalkthroughGeneratorService
{
    /// <summary>
    /// Navigates to <paramref name="appRoute"/> in a headless browser authenticated with
    /// <paramref name="jwtToken"/>, extracts the live DOM structure, sends it to the AI service,
    /// and returns driver.js-compatible tour steps.
    /// </summary>
    Task<List<WalkthroughStep>> GenerateStepsAsync(
        string appRoute,
        int moduleId,
        string jwtToken,
        CancellationToken ct);
}
