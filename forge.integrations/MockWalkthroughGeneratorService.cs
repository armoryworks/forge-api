using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Returns canned walkthrough steps for dev / mock-integrations mode.
/// No browser or AI calls are made.
/// </summary>
public class MockWalkthroughGeneratorService : IWalkthroughGeneratorService
{
    public Task<List<WalkthroughStep>> GenerateStepsAsync(
        string appRoute,
        int moduleId,
        string jwtToken,
        CancellationToken ct)
    {
        var steps = new List<WalkthroughStep>
        {
            new()
            {
                Element = null,
                Popover = new WalkthroughPopover
                {
                    Title = "Welcome to this page",
                    Description = $"This tour will walk you through the key features of {appRoute}.",
                    Side = "bottom",
                    Align = "center",
                },
            },
            new()
            {
                Element = "app-page-header",
                Popover = new WalkthroughPopover
                {
                    Title = "Page Header",
                    Description = "The page header shows the title and primary actions for this section.",
                    Side = "bottom",
                    Align = "start",
                },
            },
            new()
            {
                Element = ".action-btn--primary",
                Popover = new WalkthroughPopover
                {
                    Title = "Primary Action",
                    Description = "Click here to create a new item or perform the main action on this page.",
                    Side = "left",
                    Align = "start",
                },
            },
            new()
            {
                Element = "app-data-table",
                Popover = new WalkthroughPopover
                {
                    Title = "Data Table",
                    Description = "This table lists all records. Click any row to open the detail panel. Use column headers to sort.",
                    Side = "top",
                    Align = "center",
                },
            },
            new()
            {
                Element = null,
                Popover = new WalkthroughPopover
                {
                    Title = "You're all set!",
                    Description = "You now know the essentials. Explore on your own or revisit this tour any time from the Training module.",
                    Side = "bottom",
                    Align = "center",
                },
            },
        };

        return Task.FromResult(steps);
    }
}
