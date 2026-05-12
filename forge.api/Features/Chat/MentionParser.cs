using System.Text.RegularExpressions;

using Forge.Core.Entities;

namespace Forge.Api.Features.Chat;

public static partial class MentionParser
{
    // Matches @[entityType:entityId:displayText]
    [GeneratedRegex(@"@\[(\w+):(\d+):([^\]]+)\]")]
    private static partial Regex MentionPattern();

    public static List<ChatMessageMention> ParseMentions(string content, int chatMessageId)
    {
        var mentions = new List<ChatMessageMention>();
        var matches = MentionPattern().Matches(content);

        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[2].Value, out var entityId))
            {
                mentions.Add(new ChatMessageMention
                {
                    ChatMessageId = chatMessageId,
                    EntityType = match.Groups[1].Value,
                    EntityId = entityId,
                    DisplayText = match.Groups[3].Value,
                });
            }
        }

        return mentions;
    }
}
