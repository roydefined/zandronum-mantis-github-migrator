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
        _logger.LogInformation("Loaded {InputPath}: {IssueCount} issue(s), {CommentCount} comment(s), {AttachmentCount} attachment(s), {CloseCount} to close.",
            inputPath, issues.Count, issues.Sum(issue => issue.Comments.Count), issues.Sum(issue => issue.Attachments.Count), issues.Count(IssueUtil.IsClosed));

        var client = await GitHubClient.CreateAsync(options, _loggerFactory);

        // For the sake of clarification everything is put into a context that clarifies what is left for an issue.
        var migratedIssues = await FindMigratedIssuesAsync(client);
        var todos = issues.Select(issue => new IssueCompareContext(issue, migratedIssues.GetValueOrDefault(issue.MantisId))).ToList();
        LogTodos(todos);

        var remainingTodos = todos.Where(todo => !todo.IsDone).ToList();
        await EnsureLabelsAsync(client);

        var attachmentUrls = await EnsureReleasesAsync(client, remainingTodos);
        await MigrateIssuesAsync(client, remainingTodos, attachmentUrls);
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

    private void LogTodos(List<IssueCompareContext> todos)
    {
        // An earlier run can be cut off at any point, which leaves an issue that misses comments or wasn't closed yet.
        foreach (var todo in todos.Where(todo => !todo.NeedsCreate && !todo.IsDone))
        {
            _logger.LogInformation("Mantis #{MantisId} was cut off halfway as #{IssueNumber}: {PostedCount}/{CommentCount} comment(s) posted{Closing}.",
                todo.Issue.MantisId, todo.MigratedIssue!.Number, todo.PostedCommentCount, todo.Issue.Comments.Count, todo.NeedsClose ? " and still needs closing" : "");
        }

        _logger.LogInformation("Done: {CreatedCount} issue(s) created of which {CompleteCount} complete, {CommentCount} comment(s) posted, {ClosedCount} closed.",
            todos.Count(todo => !todo.NeedsCreate), todos.Count(todo => todo.IsDone), todos.Sum(todo => todo.PostedCommentCount),
            todos.Count(todo => IssueUtil.IsClosed(todo.Issue) && !todo.NeedsClose));

        _logger.LogInformation("To do: create {CreateCount} issue(s), finish {FinishCount}, post {CommentCount} comment(s), close {CloseCount} and upload up to {UploadCount} attachment(s).",
            todos.Count(todo => todo.NeedsCreate), todos.Count(todo => !todo.NeedsCreate && !todo.IsDone), todos.Sum(todo => todo.CommentsToPost.Count),
            todos.Count(todo => todo.NeedsClose), todos.Sum(todo => todo.AttachmentsToUpload.Count));
    }

    private async Task EnsureLabelsAsync(GitHubClient client)
    {
        _logger.LogInformation("Ensuring {LabelCount} label(s) exist.", IssueUtil.AllLabels.Count);
        await client.EnsureLabelsAsync(IssueUtil.AllLabels);
    }

    // Returns the download URL of every attachment by asset name, as GitHub reports it.
    private async Task<Dictionary<string, string>> EnsureReleasesAsync(GitHubClient client, List<IssueCompareContext> todos)
    {
        var attachmentUrls = new Dictionary<string, string>();
        var attachmentsByRelease = todos
            .SelectMany(todo => todo.AttachmentsToUpload)
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

    private async Task MigrateIssuesAsync(GitHubClient client, List<IssueCompareContext> todos, Dictionary<string, string> attachmentUrls)
    {
        var createdCount = 0;
        var finishedCount = 0;

        for (var index = 0; index < todos.Count; index++)
        {
            var todo = todos[index];
            var issue = todo.Issue;
            var commentsToPost = todo.CommentsToPost;

            // Step 1: create the issue, unless an earlier run already did.
            int issueNumber;
            if (todo.NeedsCreate)
            {
                issueNumber = await client.CreateIssueAsync(issue.Title, IssueUtil.BuildBody(issue, attachmentUrls), IssueUtil.GetLabelNames(issue));
                createdCount++;
                _logger.LogInformation("[{Index}/{Total}] Created #{IssueNumber} for Mantis #{MantisId} \"{Title}\", posting {CommentCount} comment(s).",
                    index + 1, todos.Count, issueNumber, issue.MantisId, issue.Title, commentsToPost.Count);
            }
            else
            {
                issueNumber = todo.MigratedIssue!.Number;
                finishedCount++;
                _logger.LogInformation("[{Index}/{Total}] Finishing #{IssueNumber} for Mantis #{MantisId}, posting the last {MissingCount} of {CommentCount} comment(s).",
                    index + 1, todos.Count, issueNumber, issue.MantisId, commentsToPost.Count, issue.Comments.Count);
            }

            // Step 2: post the comments that aren't on GitHub yet.
            for (var i = 0; i < commentsToPost.Count; i++)
            {
                var comment = commentsToPost[i];
                await client.CreateCommentAsync(issueNumber, IssueUtil.BuildCommentBody(comment));
                _logger.LogInformation("Posted comment {CommentIndex}/{CommentCount} by {Author} from {CreatedAt:yyyy-MM-dd} on #{IssueNumber}.",
                    todo.PostedCommentCount + i + 1, issue.Comments.Count, comment.Author, comment.CreatedAt, issueNumber);
            }

            // Step 3: close the issue last, so a closed issue always has all its comments.
            if (todo.NeedsClose)
            {
                await client.CloseIssueAsync(issueNumber);
                _logger.LogDebug("Closed #{IssueNumber}, Mantis status is {Status}.", issueNumber, issue.Status);
            }
        }

        _logger.LogInformation("Migration complete: {CreatedCount} issue(s) created, {FinishedCount} finished.", createdCount, finishedCount);
    }
}
