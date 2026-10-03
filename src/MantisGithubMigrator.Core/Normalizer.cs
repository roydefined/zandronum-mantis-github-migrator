using System.Text.RegularExpressions;
using MantisGithubMigrator.Core.MantisExport;
using MantisGithubMigrator.Core.Normalized;

namespace MantisGithubMigrator.Core;

public partial class Normalizer
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

    // GitHub allows at most 1000 assets per release, each under 2 GiB
    // See https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases
    private const int MaxAssetsPerRelease = 1000;
    private const long MaxAssetSizeBytes = 2L * 1024 * 1024 * 1024;

    public List<NormalizedIssue> Normalize(IEnumerable<MantisIssue> issues, string exportDirectory)
    {
        var normalizedIssues = new List<NormalizedIssue>();
        var attachmentCount = 0;

        foreach (var issue in issues)
        {
            if (string.IsNullOrWhiteSpace(issue.Title))
                throw new InvalidDataException($"Mantis issue #{issue.MantisId} has no title.");

            if (!StatusByMantisCode.TryGetValue(issue.Status, out var status))
                throw new InvalidDataException($"Mantis issue #{issue.MantisId} has an unrecognized status code {issue.Status}.");

            if (!PriorityByMantisCode.TryGetValue(issue.Priority, out var priority))
                throw new InvalidDataException($"Mantis issue #{issue.MantisId} has an unrecognized priority code {issue.Priority}.");

            var attachments = new List<NormalizedAttachment>();
            foreach (var attachment in issue.Attachments)
            {
                // Fill up a release with attachments, then move on to the next one
                var releaseTag = $"mantis-attachments-{attachmentCount / MaxAssetsPerRelease + 1}";
                attachments.Add(NormalizeAttachment(issue.MantisId, attachment, releaseTag, exportDirectory));
                attachmentCount++;
            }

            normalizedIssues.Add(new NormalizedIssue
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
                Attachments = attachments,
            });
        }

        return normalizedIssues;
    }

    private static NormalizedComment NormalizeComment(MantisComment comment) => new()
    {
        Author = comment.Author,
        Text = comment.Text,
        CreatedAt = comment.CreatedAt,
    };

    private static NormalizedAttachment NormalizeAttachment(int mantisId, MantisAttachment attachment, string releaseTag, string exportDirectory)
    {
        var fullPath = Path.GetFullPath(Path.Combine(exportDirectory, attachment.LocalPath));
        if (!File.Exists(fullPath))
            throw new InvalidDataException($"Mantis issue #{mantisId} attachment {attachment.FileId} ('{attachment.Filename}') was not found at '{fullPath}'.");

        var sizeBytes = new FileInfo(fullPath).Length;
        if (sizeBytes >= MaxAssetSizeBytes)
            throw new InvalidDataException($"Mantis issue #{mantisId} attachment {attachment.FileId} ('{attachment.Filename}') is {sizeBytes} bytes, over GitHub's release asset limit.");

        return new NormalizedAttachment
        {
            Filename = attachment.Filename,

            // Full path, since migrate reads the normalized output without knowing where the export is
            LocalPath = fullPath,
            FileType = attachment.FileType,
            SizeBytes = sizeBytes,
            AssetName = BuildAssetName(mantisId, attachment),
            ReleaseTag = releaseTag,
        };
    }

    // GitHub renames assets with special characters on upload, so replace them ourselves to know the final name.
    // Mantis file IDs are unique, which keeps the asset names unique within a release.
    private static string BuildAssetName(int mantisId, MantisAttachment attachment)
    {
        var safeFilename = SafeFileNameRegex().Replace(attachment.Filename, "-").Trim('.', '-');
        return $"{mantisId}-{attachment.FileId}-{safeFilename}";
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex SafeFileNameRegex();
}
