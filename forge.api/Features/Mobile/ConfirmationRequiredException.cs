namespace Forge.Api.Features.Mobile;

/// <summary>
/// Thrown when a phone action would move a job into a status that can't be
/// undone or that will queue an accounting document, and the caller has not
/// confirmed it. The exception middleware maps this to 400 with problem code
/// "confirm-required"; the app asks the user and resends with Confirmed=true
/// and the confirmed stage as ConfirmedStageId.
/// </summary>
public class ConfirmationRequiredException(string message) : Exception(message)
{
    public const string Code = "confirm-required";
}
