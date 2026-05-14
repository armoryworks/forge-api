
namespace Forge.Core.Models;

public record CreateAnnouncementTemplateRequestModel(
    string Name,
    string Content,
    AnnouncementSeverity DefaultSeverity,
    AnnouncementScope DefaultScope,
    bool DefaultRequiresAcknowledgment);
