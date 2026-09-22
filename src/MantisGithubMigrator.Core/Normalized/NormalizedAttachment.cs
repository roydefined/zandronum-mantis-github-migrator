namespace MantisGithubMigrator.Core.Normalized;

public sealed class NormalizedAttachment
{
    public string Filename { get; set; } = string.Empty;

    public string LocalPath { get; set; } = string.Empty;

    public string FileType { get; set; } = string.Empty;
}
