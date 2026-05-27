using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>F-13-ANN-01: payload for editing a published announcement.</summary>
public record UpdateAnnouncementRequestModel(
    string Title,
    string Content,
    AnnouncementSeverity Severity,
    bool RequiresAcknowledgment,
    DateTimeOffset? ExpiresAt);
