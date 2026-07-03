namespace Forge.Core.Interfaces;

/// <summary>
/// compliance-calendar A-2. Resolves which calendar Super-Groups the current user may
/// see, so event reads can be filtered server-side. Admin (and system/background
/// contexts) are unrestricted.
/// </summary>
public interface ICalendarVisibilityService
{
    /// <summary>
    /// Super-Group ids the current user may see. <c>null</c> means <b>unrestricted</b>
    /// (Admin role, or a null current user such as a Hangfire/system context).
    /// Otherwise: the default-visible groups unioned with any groups explicitly granted
    /// to one of the user's roles.
    /// </summary>
    Task<IReadOnlyList<int>?> GetVisibleSuperGroupIdsAsync(CancellationToken ct = default);
}
