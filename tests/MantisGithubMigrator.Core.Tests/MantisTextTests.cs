namespace MantisGithubMigrator.Core.Tests;

public class MantisTextTests
{
    [Theory]
    [InlineData("[b]text[/b]", "**text**")]
    [InlineData("[B]text[/B]", "**text**")]
    [InlineData("[i]text[/i]", "*text*")]
    [InlineData("[u]text[/u]", "text")]
    [InlineData("[color=#123456]text[/color]", "text")]
    [InlineData("[url=https://example.com/a]link text[/url]", "[link text](https://example.com/a)")]
    [InlineData("[url]https://example.com/a[/url]", "https://example.com/a")]
    [InlineData("[img]https://example.com/a.png[/img]", "![](https://example.com/a.png)")]
    public void ConvertsBbCode(string mantis, string expected)
    {
        Assert.Equal(expected, MantisText.ToMarkdown(mantis));
    }

    [Theory]
    [InlineData("see #1.", "see `#1`.")]
    [InlineData("#1 at the start", "`#1` at the start")]
    [InlineData("https://example.com/a#1", "https://example.com/a#1")]
    [InlineData("word#1", "word#1")]
    [InlineData("a&#1;b", "a&#1;b")]
    [InlineData("# not a reference", "# not a reference")]
    public void KeepsIssueReferencesFromLinkingOnGitHub(string mantis, string expected)
    {
        Assert.Equal(expected, MantisText.ToMarkdown(mantis));
    }

    [Theory]
    [InlineData("hi @user", "hi `@user`")]
    [InlineData("@user at the start", "`@user` at the start")]
    [InlineData("name@example.com", "name@example.com")]
    [InlineData("https://name@example.com/", "https://name@example.com/")]
    public void KeepsMentionsFromNotifyingGitHubUsers(string mantis, string expected)
    {
        Assert.Equal(expected, MantisText.ToMarkdown(mantis));
    }

    [Theory]
    [InlineData("a <placeholder> b", "a &lt;placeholder> b")]
    [InlineData("<@user> said", "&lt;`@user`> said")]
    public void KeepsAngleBracketsVisible(string mantis, string expected)
    {
        Assert.Equal(expected, MantisText.ToMarkdown(mantis));
    }

    [Theory]
    [InlineData("[note] plain text")]
    [InlineData("0000001:0000002")]
    [InlineData("~1")]
    public void LeavesTextThatOnlyLooksLikeMarkup(string mantis)
    {
        Assert.Equal(mantis, MantisText.ToMarkdown(mantis));
    }

    [Fact]
    public void ConvertsQuoteWithAuthor()
    {
        Assert.Equal(
            "> **Alice wrote:**\n> line one\n> line two\n\n\nreply",
            MantisText.ToMarkdown("[quote=Alice]line one\nline two[/quote]\nreply"));
    }

    [Fact]
    public void ConvertsQuoteWithoutAuthor()
    {
        Assert.Equal("> quoted", MantisText.ToMarkdown("[quote]quoted[/quote]"));
    }

    [Fact]
    public void ConvertsNestedQuotes()
    {
        Assert.Equal(
            "> **Alice wrote:**\n> outer \n> > **Bob wrote:**\n> > inner\n> \n>  after",
            MantisText.ToMarkdown("[quote=Alice]outer [quote=Bob]inner[/quote] after[/quote]"));
    }

    [Fact]
    public void KeepsCodeBlocksUntouched()
    {
        Assert.Equal(
            "```\n[b]a[/b] #1 @user <tag>\n```",
            MantisText.ToMarkdown("[code][b]a[/b] #1 @user <tag>[/code]"));
    }

    [Fact]
    public void ConvertsTextAroundCodeBlocks()
    {
        Assert.Equal(
            "before `#1`\n\n```\ncode #1\n```\n\nafter `@user`",
            MantisText.ToMarkdown("before #1\n[code]\ncode #1\n[/code]\nafter @user"));
    }

    [Fact]
    public void RemovesColorsInsideCodeBlocks()
    {
        Assert.Equal(
            "```\nfile | 3 ++-\n```",
            MantisText.ToMarkdown("[code]\nfile | 3 [color=#00FF00]++[/color][color=#FF0000]-[/color]\n[/code]"));
    }

    [Fact]
    public void NormalizesLineEndingsAndTrims()
    {
        Assert.Equal("first\nsecond", MantisText.ToMarkdown("\r\nfirst\r\nsecond\r\n"));
    }

    [Fact]
    public void KeepsEmptyTextEmpty()
    {
        Assert.Equal(string.Empty, MantisText.ToMarkdown(string.Empty));
    }
}
