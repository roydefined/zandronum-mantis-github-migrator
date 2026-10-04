using MantisGithubMigrator.GitHub;
using Microsoft.Extensions.Logging;

namespace MantisGithubMigrator.Cli.Commands;

public class CloseCommand
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CloseCommand> _logger;

    public CloseCommand(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CloseCommand>();
    }

    public async Task RunAsync(GitHubClientOptions options)
    {
        var client = await GitHubClient.CreateAsync(options, _loggerFactory);

        var issueNumbers = await client.ListIssueNumbersByLabelAsync(IssueUtil.ImportLabelName);
        _logger.LogInformation("Found {IssueCount} migrated issue(s) on {Owner}/{Repo}.", issueNumbers.Count, options.Owner, options.Repo);

        foreach (var number in issueNumbers)
        {
            await client.CloseIssueAsync(number);
            _logger.LogInformation("Closed #{IssueNumber}.", number);
        }

        _logger.LogInformation("Close complete: closed {IssueCount} migrated issue(s).", issueNumbers.Count);
    }
}
