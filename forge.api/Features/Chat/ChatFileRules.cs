using Microsoft.AspNetCore.Http;

namespace Forge.Api.Features.Chat;

public static class ChatFileRules
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const string DirectMessageEntityType = "chat-dm";

    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.Ordinal)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".gif",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt",
    };

    public static bool HasAllowedExtension(IFormFile? file)
    {
        if (file is null) return true;
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        return ext is not null && AllowedExtensions.Contains(ext);
    }
}
