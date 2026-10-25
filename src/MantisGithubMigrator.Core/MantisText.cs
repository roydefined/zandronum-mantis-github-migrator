using System.Text.RegularExpressions;

namespace MantisGithubMigrator.Core;

// Converts Mantis text with BBCode into GitHub markdown.
public static partial class MantisText
{
    public static string ToMarkdown(string text)
    {
        text = text.Replace("\r\n", "\n");

        // Markdown has no colors. Mantis also used them in code blocks, so remove them everywhere.
        text = ColorRegex().Replace(text, string.Empty);

        // Leave code blocks as is and only convert the text around them.
        var parts = CodeBlockRegex().Split(text);
        for (var i = 0; i < parts.Length; i++)
        {
            // Every odd part is the code captured by the split.
            parts[i] = i % 2 == 1
                ? $"\n```\n{parts[i].Trim('\n')}\n```\n"
                : ConvertText(parts[i]);
        }

        return string.Concat(parts).Trim();
    }

    private static string ConvertText(string text)
    {
        // GitHub hides anything that looks like an HTML tag, like <ip address>.
        text = text.Replace("<", "&lt;");

        // Markdown has no underline.
        text = UnderlineRegex().Replace(text, string.Empty);

        text = BoldRegex().Replace(text, "**$1**");
        text = ItalicRegex().Replace(text, "*$1*");
        text = NamedUrlRegex().Replace(text, "[$2]($1)");
        text = UrlRegex().Replace(text, "$1");
        text = ImageRegex().Replace(text, "![]($1)");
        text = ConvertQuotes(text);

        // Code formatting stops GitHub from linking #123 to our own issues and notifying @name.
        text = IssueReferenceRegex().Replace(text, "`$0`");
        text = MentionRegex().Replace(text, "`$0`");

        return text;
    }

    // Quotes can be nested, so keep converting the innermost one until none are left.
    private static string ConvertQuotes(string text)
    {
        while (QuoteRegex().IsMatch(text))
        {
            text = QuoteRegex().Replace(text, match =>
            {
                var author = match.Groups[1].Value;
                var lines = match.Groups[2].Value.Trim('\n').Split('\n');
                var header = author.Length > 0 ? $"> **{author} wrote:**\n" : string.Empty;
                return $"\n{header}{string.Join("\n", lines.Select(line => $"> {line}"))}\n\n";
            });
        }

        return text;
    }

    [GeneratedRegex(@"\[code\](.*?)\[/code\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"\[/?color(=[^\]]*)?\]", RegexOptions.IgnoreCase)]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"\[/?u\]", RegexOptions.IgnoreCase)]
    private static partial Regex UnderlineRegex();

    [GeneratedRegex(@"\[b\](.*?)\[/b\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\[i\](.*?)\[/i\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"\[url=([^\]]+)\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NamedUrlRegex();

    [GeneratedRegex(@"\[url\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\[img\](.*?)\[/img\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImageRegex();

    // Only matches a quote without another quote inside.
    [GeneratedRegex(@"\[quote(?:=([^\]]*))?\]((?:(?!\[quote).)*?)\[/quote\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex QuoteRegex();

    // Skips URLs and HTML entities.
    [GeneratedRegex(@"(?<![\w/&])#\d+\b")]
    private static partial Regex IssueReferenceRegex();

    // Skips e-mail addresses and URLs.
    [GeneratedRegex(@"(?<![\w/])@[A-Za-z0-9][A-Za-z0-9_-]*")]
    private static partial Regex MentionRegex();
}
