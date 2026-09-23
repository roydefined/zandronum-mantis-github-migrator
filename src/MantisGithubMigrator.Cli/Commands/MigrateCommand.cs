using System.Text.Json;
using System.Text.Json.Serialization;
using MantisGithubMigrator.Core.Normalized;
using MantisGithubMigrator.GitHub;

namespace MantisGithubMigrator.Cli.Commands;

public class MigrateCommand
{
    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task RunAsync(string inputPath, GitHubClientOptions options)
    {
        var json = File.ReadAllText(inputPath);
        var issues = JsonSerializer.Deserialize<List<NormalizedIssue>>(json, InputJsonOptions)
            ?? throw new InvalidDataException($"'{inputPath}' did not contain normalized issues.");

        var client = new GitHubClient(options);

        Console.WriteLine($"Ensuring labels on {options.Owner}/{options.Repo}...");
        await client.EnsureLabelsAsync(IssueUtil.AllLabels);

        foreach (var issue in issues)
        {
            var labelNames = IssueUtil.GetLabelNames(issue);

            var issueNumber = await client.CreateIssueAsync(
                issue.Title,
                IssueUtil.BuildBody(issue),
                labelNames,
                IssueUtil.IsClosed(issue));

            foreach (var comment in issue.Comments)
                await client.CreateCommentAsync(issueNumber, IssueUtil.BuildCommentBody(comment));

            Console.WriteLine(
                $"Migrated Mantis #{issue.MantisId} -> {options.Owner}/{options.Repo}#{issueNumber} " +
                $"({issue.Comments.Count} comment(s)).");
        }

        Console.WriteLine($"Migration complete: {issues.Count} issue(s) migrated.");
    }
}
