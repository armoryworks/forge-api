
namespace Forge.Core.Models;

public record AnnouncementTemplateResponseModel(
    int Id,
    string Name,
    string Content,
    AnnouncementSeverity DefaultSeverity,
    AnnouncementScope DefaultScope,
    bool DefaultRequiresAcknowledgment);
