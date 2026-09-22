using MantisGithubMigrator.Core.MantisExport;
using MantisGithubMigrator.Core.Normalized;

namespace MantisGithubMigrator.Core;

public class Normalizer
{
    private static readonly Dictionary<int, IssuePriority> PriorityByMantisCode = new()
    {
        [10] = IssuePriority.None,
        [20] = IssuePriority.Low,
        [30] = IssuePriority.Normal,
        [40] = IssuePriority.High,
        [50] = IssuePriority.Urgent,
        [60] = IssuePriority.Immediate,
    };

    private static readonly Dictionary<int, IssueStatus> StatusByMantisCode = new()
    {
        [10] = IssueStatus.New,
        [20] = IssueStatus.Feedback,
        [30] = IssueStatus.Acknowledged,
        [40] = IssueStatus.Confirmed,
        [50] = IssueStatus.Assigned,
        [80] = IssueStatus.Resolved,
        [90] = IssueStatus.Closed,
    };

    public List<NormalizedIssue> Normalize(IEnumerable<MantisIssue> issues)
        => issues.Select(Normalize).ToList();

    private static NormalizedIssue Normalize(MantisIssue issue)
    {
        if (string.IsNullOrWhiteSpace(issue.Title))
            throw new InvalidDataException($"Mantis issue #{issue.MantisId} has no title.");

        if (!StatusByMantisCode.TryGetValue(issue.Status, out var status))
            throw new InvalidDataException($"Mantis issue #{issue.MantisId} has an unrecognized status code {issue.Status}.");

        if (!PriorityByMantisCode.TryGetValue(issue.Priority, out var priority))
            throw new InvalidDataException($"Mantis issue #{issue.MantisId} has an unrecognized priority code {issue.Priority}.");

        return new NormalizedIssue
        {
            MantisId = issue.MantisId,
            Title = issue.Title,
            Description = issue.Description,
            StepsToReproduce = issue.StepsToReproduce,
            AdditionalInformation = issue.AdditionalInformation,
            Author = issue.Reporter,
            CreatedAt = issue.CreatedAt,
            UpdatedAt = issue.UpdatedAt,
            Status = status,
            Priority = priority,
            Comments = issue.Comments.Select(NormalizeComment).ToList(),
            Attachments = issue.Attachments.Select(NormalizeAttachment).ToList(),
        };
    }

    private static NormalizedComment NormalizeComment(MantisComment comment) => new()
    {
        Author = comment.Author,
        Text = comment.Text,
        CreatedAt = comment.CreatedAt,
    };

    private static NormalizedAttachment NormalizeAttachment(MantisAttachment attachment) => new()
    {
        Filename = attachment.Filename,
        LocalPath = attachment.LocalPath,
        FileType = attachment.FileType,
    };
}
