namespace Forge.Core.Models;

/// <summary>Driver.js tour step — matches the client-side DriverStep interface.</summary>
public record WalkthroughStep
{
    /// <summary>CSS selector of the element to highlight, or null for a centered dialog step.</summary>
    public string? Element { get; init; }
    public WalkthroughPopover Popover { get; init; } = new();
}

public record WalkthroughPopover
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    /// <summary>top | bottom | left | right</summary>
    public string Side { get; init; } = "bottom";
    /// <summary>start | center | end</summary>
    public string Align { get; init; } = "start";
}
