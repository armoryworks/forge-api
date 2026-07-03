namespace Forge.Core.Models;

/// <summary>compliance-calendar A-3: create/update payload for a personal saved layer view.</summary>
public record CalendarSavedViewRequestModel(
    string Name,
    string Scope,
    int[] SelectedSuperGroupIds,
    int[] SelectedEventTypeIds);
