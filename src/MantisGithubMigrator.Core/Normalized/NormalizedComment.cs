namespace MantisGithubMigrator.Core.Normalized;

public sealed class NormalizedComment
{
    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
