using System.Text.Json;
using System.Text.Json.Serialization;
using MantisGithubMigrator.Core.Normalized;
using MantisGithubMigrator.GitHub;
using Microsoft.Extensions.Logging;

namespace MantisGithubMigrator.Cli.Commands;

public class MigrateCommand
{
    private const string ReleaseBody = "Attachments migrated from the Zandronum MantisBT tracker. This is not a software release.";

    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MigrateCommand> _logger;

    public MigrateCommand(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MigrateCommand>();
    }

    public async Task RunAsync(string inputPath, GitHubClientOptions options)
    {
        var json = File.ReadAllText(inputPath);
        var issues = JsonSerializer.Deserialize<List<NormalizedIssue>>(json, InputJsonOptions)
            ?? throw new InvalidDataException($"'{inputPath}' did not contain normalized issues.");
        _logger.LogInformation("Loaded {IssueCount} issue(s) from {InputPath}.", issues.Count, inputPath);

        var client = await GitHubClient.CreateAsync(options, _loggerFactory);

        var migratedIssues = await FindMigratedIssuesAsync(client);
        var remainingIssues = SelectRemainingIssues(issues, migratedIssues);

        await EnsureLabelsAsync(client);
        var attachmentUrls = await EnsureReleasesAsync(client, remainingIssues);
        await MigrateIssuesAsync(client, remainingIssues, migratedIssues, attachmentUrls);
    }

    // Finds what's already on GitHub through the tracking marker, so a rerun doesn't create duplicates.
    private async Task<Dictionary<int, MigratedIssue>> FindMigratedIssuesAsync(GitHubClient client)
    {
        var migratedIssues = await client.ListMigratedIssuesAsync();
        _logger.LogInformation("Found {IssueCount} migrated issue(s) on GitHub.", migratedIssues.Count);

        // As a failsafe we filter out duplicates that have the lesser amount of comments.
        // As we migrate in order these issues should not be relevant; only one needs to continue and have its comments filled in when incomplete.
        var issuesByMantisId = migratedIssues.GroupBy(issue => issue.MantisId).ToList();
        foreach (var duplicates in issuesByMantisId.Where(group => group.Count() > 1))
        {
            _logger.LogWarning("Mantis #{MantisId} exists {Count} times on GitHub ({IssueNumbers}), continuing with #{IssueNumber}.",
                duplicates.Key, duplicates.Count(), string.Join(", ", duplicates.Select(issue => $"#{issue.Number}")), duplicates.MaxBy(issue => issue.CommentCount)!.Number);
        }

        return issuesByMantisId.ToDictionary(group => group.Key, group => group.MaxBy(issue => issue.CommentCount)!);
    }

    // Leaves out every issue that's fully migrated already, and tells which ones were cut off halfway.
    private List<NormalizedIssue> SelectRemainingIssues(List<NormalizedIssue> issues, Dictionary<int, MigratedIssue> migratedIssues)
    {
        var remainingIssues = new List<NormalizedIssue>();
        var skippedCount = 0;
        var incompleteCount = 0;

        foreach (var issue in issues)
        {
            if (IsComplete(issue, migratedIssues))
            {
                _logger.LogDebug("Skipping Mantis #{MantisId}, fully migrated as #{IssueNumber}.", issue.MantisId, migratedIssues[issue.MantisId].Number);
                skippedCount++;
                continue;
            }

            if (migratedIssues.TryGetValue(issue.MantisId, out var migratedIssue))
            {
                _logger.LogInformation("Mantis #{MantisId} was cut off halfway as #{IssueNumber}: {PostedCount}/{CommentCount} comment(s) posted{Closing}.",
                    issue.MantisId, migratedIssue.Number, migratedIssue.CommentCount, issue.Comments.Count,
                    IssueUtil.IsClosed(issue) && !migratedIssue.IsClosed ? " and still needs closing" : "");
                incompleteCount++;
            }

            remainingIssues.Add(issue);
        }

        _logger.LogInformation("{SkippedCount} issue(s) already migrated, {IncompleteCount} to finish, {NewCount} to create.",
            skippedCount, incompleteCount, remainingIssues.Count - incompleteCount);
        return remainingIssues;
    }

    // Tells whether an issue was fully migrated by an earlier run, so it can be skipped entirely.
    // A run can be cut off at any point, which leaves an issue that exists but misses comments or wasn't closed yet.
    // Comments are posted in order and the issue is closed last, so comparing those two is enough to spot that.
    private static bool IsComplete(NormalizedIssue issue, Dictionary<int, MigratedIssue> migratedIssues)
    {
        // Not on GitHub at all yet.
        if (!migratedIssues.TryGetValue(issue.MantisId, out var migratedIssue))
            return false;

        var hasAllComments = migratedIssue.CommentCount >= issue.Comments.Count;

        // Only a missing close counts. An issue that should stay open but was closed by hand on GitHub is left alone.
        var hasCorrectState = migratedIssue.IsClosed || !IssueUtil.IsClosed(issue);

        return hasAllComments && hasCorrectState;
    }

    private async Task EnsureLabelsAsync(GitHubClient client)
    {
        _logger.LogInformation("Ensuring {LabelCount} label(s) exist.", IssueUtil.AllLabels.Count);
        await client.EnsureLabelsAsync(IssueUtil.AllLabels);
    }

    // Returns the download URL of every attachment by asset name, as GitHub reports it.
    private async Task<Dictionary<string, string>> EnsureReleasesAsync(GitHubClient client, List<NormalizedIssue> issues)
    {
        var attachmentUrls = new Dictionary<string, string>();
        var attachmentsByRelease = issues
            .SelectMany(issue => issue.Attachments)
            .GroupBy(attachment => attachment.ReleaseTag)
            .ToList();

        if (attachmentsByRelease.Count == 0)
        {
            _logger.LogInformation("No attachments to upload.");
            return attachmentUrls;
        }

        foreach (var attachments in attachmentsByRelease)
        {
            // Always pass the name body, to keep it clear this is always a separate release.
            var release = await client.EnsureReleaseAsync(attachments.Key, ReleaseBody);
            var existingUrls = await client.ListAssetUrlsAsync(release);

            var missingCount = attachments.Count(attachment => !existingUrls.ContainsKey(attachment.AssetName));
            _logger.LogInformation("Release {Tag}: {PresentCount}/{AttachmentCount} attachment(s) already uploaded, {MissingCount} to upload.",
                attachments.Key, attachments.Count() - missingCount, attachments.Count(), missingCount);

            // Upload attachments, filtering out whatever we already have.
            foreach (var attachment in attachments)
            {
                if (existingUrls.TryGetValue(attachment.AssetName, out var existingUrl))
                {
                    attachmentUrls[attachment.AssetName] = existingUrl;
                    continue;
                }

                var contentType = string.IsNullOrWhiteSpace(attachment.FileType) ? "application/octet-stream" : attachment.FileType;
                attachmentUrls[attachment.AssetName] = await client.UploadAssetAsync(release, attachment.AssetName, contentType, attachment.LocalPath);

                _logger.LogInformation("Uploaded {AssetName} ({SizeKilobytes} KB) to {Tag}.", attachment.AssetName, attachment.SizeBytes / 1024, attachments.Key);
            }
        }

        return attachmentUrls;
    }

    private async Task MigrateIssuesAsync(
        GitHubClient client, List<NormalizedIssue> issues, Dictionary<int, MigratedIssue> migratedIssues, Dictionary<string, string> attachmentUrls)
    {
        var createdCount = 0;
        var finishedCount = 0;

        for (var index = 0; index < issues.Count; index++)
        {
            var issue = issues[index];

            // Set when an earlier run was cut off halfway through this issue.
            var migratedIssue = migratedIssues.GetValueOrDefault(issue.MantisId);
            var postedCount = migratedIssue?.CommentCount ?? 0;

            // Step 1: create the issue, unless an earlier run already did.
            int issueNumber;
            if (migratedIssue is null)
            {
                issueNumber = await client.CreateIssueAsync(issue.Title, IssueUtil.BuildBody(issue, attachmentUrls), IssueUtil.GetLabelNames(issue));
                createdCount++;
                _logger.LogInformation("[{Index}/{Total}] Created #{IssueNumber} for Mantis #{MantisId} \"{Title}\", posting {CommentCount} comment(s).",
                    index + 1, issues.Count, issueNumber, issue.MantisId, issue.Title, issue.Comments.Count);
            }
            else
            {
                issueNumber = migratedIssue.Number;
                finishedCount++;
                _logger.LogInformation("[{Index}/{Total}] Finishing #{IssueNumber} for Mantis #{MantisId}, posting the last {MissingCount} of {CommentCount} comment(s).",
                    index + 1, issues.Count, issueNumber, issue.MantisId, Math.Max(0, issue.Comments.Count - postedCount), issue.Comments.Count);
            }

            // Step 2: post the comments that aren't on GitHub yet. They're posted in order, so those are the last ones.
            for (var commentIndex = postedCount; commentIndex < issue.Comments.Count; commentIndex++)
            {
                var comment = issue.Comments[commentIndex];
                await client.CreateCommentAsync(issueNumber, IssueUtil.BuildCommentBody(comment));
                _logger.LogInformation("Posted comment {CommentIndex}/{CommentCount} by {Author} from {CreatedAt:yyyy-MM-dd} on #{IssueNumber}.",
                    commentIndex + 1, issue.Comments.Count, comment.Author, comment.CreatedAt, issueNumber);
            }

            // Step 3: close the issue last, so a closed issue always has all its comments.
            if (IssueUtil.IsClosed(issue) && migratedIssue?.IsClosed != true)
            {
                await client.CloseIssueAsync(issueNumber);
                _logger.LogDebug("Closed #{IssueNumber}, Mantis status is {Status}.", issueNumber, issue.Status);
            }
        }

        _logger.LogInformation("Migration complete: {CreatedCount} issue(s) created, {FinishedCount} finished.", createdCount, finishedCount);
    }
}
