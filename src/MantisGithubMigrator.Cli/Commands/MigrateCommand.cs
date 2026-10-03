using System.Text.Json;
using System.Text.Json.Serialization;
using MantisGithubMigrator.Core.Normalized;
using MantisGithubMigrator.GitHub;

namespace MantisGithubMigrator.Cli.Commands;

public class MigrateCommand
{
    private const string ReleaseBody = "Attachments migrated from the Zandronum MantisBT tracker. This is not a software release.";

    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task RunAsync(string inputPath, GitHubClientOptions options)
    {
        var json = File.ReadAllText(inputPath);
        var issues = JsonSerializer.Deserialize<List<NormalizedIssue>>(json, InputJsonOptions)
            ?? throw new InvalidDataException($"'{inputPath}' did not contain normalized issues.");

        var client = await GitHubClient.CreateAsync(options, Console.WriteLine);

        var migratedIssues = await FindMigratedIssuesAsync(client, options);
        var remainingIssues = issues.Where(issue => !IsComplete(issue, migratedIssues)).ToList();
        Console.WriteLine($"{issues.Count - remainingIssues.Count} issue(s) already migrated, {remainingIssues.Count} to go.");

        await EnsureLabelsAsync(client, options);
        var attachmentUrls = await EnsureReleasesAsync(client, options, remainingIssues);
        await MigrateIssuesAsync(client, options, remainingIssues, migratedIssues, attachmentUrls);
    }

    // Finds what's already on GitHub through the tracking marker, so a rerun doesn't create duplicates.
    private static async Task<Dictionary<int, MigratedIssue>> FindMigratedIssuesAsync(GitHubClient client, GitHubClientOptions options)
    {
        Console.WriteLine($"Checking for issues already migrated to {options.Owner}/{options.Repo}...");

        // As a failsafe we filter out duplicates that have the lesser amount of comments.
        // As we migrate in order these issues should not be relevant; only one needs to continue and have its comments filled in when incomplete.
        var migratedIssues = await client.ListMigratedIssuesAsync();
        return migratedIssues
            .GroupBy(issue => issue.MantisId)
            .ToDictionary(group => group.Key, group => group.MaxBy(issue => issue.CommentCount)!);
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

    private static async Task EnsureLabelsAsync(GitHubClient client, GitHubClientOptions options)
    {
        Console.WriteLine($"Ensuring labels on {options.Owner}/{options.Repo}...");
        await client.EnsureLabelsAsync(IssueUtil.AllLabels);
    }

    // Returns the download URL of every attachment by asset name, as GitHub reports it.
    private static async Task<Dictionary<string, string>> EnsureReleasesAsync(GitHubClient client, GitHubClientOptions options, List<NormalizedIssue> issues)
    {
        var attachmentUrls = new Dictionary<string, string>();
        var attachmentsByRelease = issues
            .SelectMany(issue => issue.Attachments)
            .GroupBy(attachment => attachment.ReleaseTag);

        foreach (var attachments in attachmentsByRelease)
        {
            Console.WriteLine($"Ensuring release {attachments.Key} on {options.Owner}/{options.Repo}...");

            // Always pass the name body, to keep it clear this is always a separate release.
            var release = await client.EnsureReleaseAsync(attachments.Key, ReleaseBody);
            var existingUrls = await client.ListAssetUrlsAsync(release);

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

                Console.WriteLine($"Uploaded {attachment.AssetName} to {attachments.Key}.");
            }
        }

        return attachmentUrls;
    }

    private static async Task MigrateIssuesAsync(
        GitHubClient client, GitHubClientOptions options, List<NormalizedIssue> issues, Dictionary<int, MigratedIssue> migratedIssues, Dictionary<string, string> attachmentUrls)
    {
        foreach (var issue in issues)
        {
            // Set when an earlier run was cut off halfway through this issue.
            var migratedIssue = migratedIssues.GetValueOrDefault(issue.MantisId);

            // Step 1: create the issue, unless an earlier run already did.
            var issueNumber = migratedIssue?.Number
                ?? await client.CreateIssueAsync(issue.Title, IssueUtil.BuildBody(issue, attachmentUrls), IssueUtil.GetLabelNames(issue));

            // Step 2: post the comments that aren't on GitHub yet. They're posted in order, so those are the last ones.
            foreach (var comment in issue.Comments.Skip(migratedIssue?.CommentCount ?? 0))
                await client.CreateCommentAsync(issueNumber, IssueUtil.BuildCommentBody(comment));

            // Step 3: close the issue last, so a closed issue always has all its comments.
            if (IssueUtil.IsClosed(issue) && migratedIssue?.IsClosed != true)
                await client.CloseIssueAsync(issueNumber);

            Console.WriteLine(
                $"Migrated Mantis #{issue.MantisId} -> {options.Owner}/{options.Repo}#{issueNumber} " +
                $"({issue.Comments.Count} comment(s), {issue.Attachments.Count} attachment(s)).");
        }

        Console.WriteLine($"Migration complete: {issues.Count} issue(s) migrated.");
    }
}
