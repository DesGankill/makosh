using System.Text;
using System.Text.RegularExpressions;

namespace Makosh.Core;

public static class SileroText
{
    static readonly Regex Markdown = new(@"(\*\*|__|\*|_|`+|#+)", RegexOptions.Compiled);
    static readonly Regex Url = new(@"https?://\S+|www\.\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Fence = new(@"```[\s\S]*?```", RegexOptions.Compiled);
    static readonly Regex WinPath = new(@"[A-Za-z]:\\[^\s]+", RegexOptions.Compiled);
    static readonly Regex LatinRun = new(@"[A-Za-z][A-Za-z0-9_.-]*", RegexOptions.Compiled);

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var value = Fence.Replace(text, " код ");
        value = Url.Replace(value, " ссылка ");
        value = WinPath.Replace(value, " путь ");
        value = Markdown.Replace(value, " ");
        value = LatinRun.Replace(value, SpeakLatin);
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' or '"' or '\'' or '«' or '»')
            {
                builder.Append(' ');
                continue;
            }

            if (ch is '_' or '/' or '\\' or '|' or '=' or '+' or '*' or '#' or '@' or '&' or '%' or '$' or '^' or '~')
            {
                builder.Append(' ');
                continue;
            }

            if (ch <= 127 && char.IsAsciiLetter(ch))
            {
                builder.Append(' ');
                continue;
            }

            if (ch <= 127 && !char.IsDigit(ch) && !char.IsWhiteSpace(ch) &&
                ch is not '.' and not ',' and not '!' and not '?' and not ':' and not ';' and not '-')
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(ch);
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    static string SpeakLatin(Match match)
    {
        var word = match.Value.ToLowerInvariant();
        return word switch
        {
            "youtube" => "ютуб",
            "blender" => "блендер",
            "steam" => "стим",
            "discord" => "дискорд",
            "valheim" => "вальхейм",
            "ocr" => "оцр",
            "api" => "апи",
            "url" => "адрес",
            "http" or "https" => "ссылка",
            "look_screen" or "lookscreen" => "просмотр экрана",
            "vision" => "зрение",
            "ctrl" => "контроль",
            "shift" => "шифт",
            "enter" => "энтер",
            "ok" => "окей",
            _ when word.Length <= 2 => " ",
            _ => " название ",
        };
    }
}
