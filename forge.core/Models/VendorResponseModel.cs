namespace Forge.Core.Models;

/// <summary>
/// Compact vendor projection used by dropdown pickers. <c>IsActive</c> is
/// surfaced (Phase 3 H2 / WU-12) so the UI can grey-out and label
/// "(deactivated)" entries, and the active-check on the server-side has a
/// matching client-side hint.
/// </summary>
public record VendorResponseModel(int Id, string CompanyName, bool IsActive);
