using MantisGithubMigrator.GitHub;

namespace MantisGithubMigrator.Cli.Commands;

public class CloseCommand
{
    public async Task RunAsync(GitHubClientOptions options)
    {
        var client = await GitHubClient.CreateAsync(options);

        var issueNumbers = await client.ListIssueNumbersByLabelAsync(IssueUtil.ImportLabelName);

        foreach (var number in issueNumbers)
        {
            await client.CloseIssueAsync(number);
            Console.WriteLine($"Closed {options.Owner}/{options.Repo}#{number}.");
        }

        Console.WriteLine($"Close complete: closed {issueNumbers.Count} migrated issue(s) in {options.Owner}/{options.Repo}.");
    }
}
