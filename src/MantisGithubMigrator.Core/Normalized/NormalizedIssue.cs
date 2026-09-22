namespace MantisGithubMigrator.Core.Normalized;

public sealed class NormalizedIssue
{
    public int MantisId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string StepsToReproduce { get; set; } = string.Empty;

    public string AdditionalInformation { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public IssueStatus Status { get; set; }

    public IssuePriority Priority { get; set; }

    public List<NormalizedComment> Comments { get; set; } = [];

    public List<NormalizedAttachment> Attachments { get; set; } = [];
}
