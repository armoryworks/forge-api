namespace Forge.Core.Enums;

public enum ReorderSuggestionStatus
{
    Pending = 0,
    Approved = 1,
    Dismissed = 2,
    Expired = 3,   // auto-closed when stock rises above reorder point
}
