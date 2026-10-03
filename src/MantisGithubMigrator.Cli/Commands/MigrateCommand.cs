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

        await EnsureLabelsAsync(client, options);
        var attachmentUrls = await EnsureReleasesAsync(client, options, issues);
        await MigrateIssuesAsync(client, options, issues, attachmentUrls);
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

    private static async Task MigrateIssuesAsync(GitHubClient client, GitHubClientOptions options, List<NormalizedIssue> issues, Dictionary<string, string> attachmentUrls)
    {
        foreach (var issue in issues)
        {
            var labelNames = IssueUtil.GetLabelNames(issue);

            var issueNumber = await client.CreateIssueAsync(
                issue.Title,
                IssueUtil.BuildBody(issue, attachmentUrls),
                labelNames,
                IssueUtil.IsClosed(issue));

            foreach (var comment in issue.Comments)
                await client.CreateCommentAsync(issueNumber, IssueUtil.BuildCommentBody(comment));

            Console.WriteLine(
                $"Migrated Mantis #{issue.MantisId} -> {options.Owner}/{options.Repo}#{issueNumber} " +
                $"({issue.Comments.Count} comment(s), {issue.Attachments.Count} attachment(s)).");
        }

        Console.WriteLine($"Migration complete: {issues.Count} issue(s) migrated.");
    }
}
