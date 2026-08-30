namespace Forge.Api.Services;

/// <summary>
/// Collapses duplicate scan events: the same device, code, and action inside
/// a three-second window is one event. Returns true when the event is a
/// duplicate that should be ignored (and logged), false when it is fresh.
/// </summary>
public interface IScanCollapseService
{
    bool IsDuplicate(string deviceKey, string code, string action);
}
