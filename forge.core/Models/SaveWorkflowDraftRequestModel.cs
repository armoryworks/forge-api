using System.Text.Json;

namespace Forge.Core.Models;

/// <summary>
/// What the user has typed on an entity-less workflow run that has not been
/// saved through a step yet (for a Part: part number, name, description).
/// Held on the run only so draft lists can label the draft and the form can
/// be refilled on resume; it never feeds entity creation.
/// </summary>
public record SaveWorkflowDraftRequestModel(JsonElement Fields);
