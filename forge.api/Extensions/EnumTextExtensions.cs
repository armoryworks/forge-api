using System.Text;

namespace Forge.Api.Extensions;

public static class EnumTextExtensions
{
    public static string ToSentence(this Enum value)
    {
        var name = value.ToString();
        var text = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                var acronym = char.IsUpper(name[i - 1]) && (i + 1 >= name.Length || !char.IsLower(name[i + 1]));
                if (!acronym) text.Append(' ');
                text.Append(acronym || (i + 1 < name.Length && char.IsUpper(name[i + 1])) ? c : char.ToLowerInvariant(c));
                continue;
            }
            text.Append(c);
        }
        return text.ToString();
    }
}
