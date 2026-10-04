using MantisGithubMigrator.GitHub;
using Microsoft.Extensions.Logging;

namespace MantisGithubMigrator.Cli.Commands;

public class DiscardCommand
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DiscardCommand> _logger;

    public DiscardCommand(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DiscardCommand>();
    }

    public async Task RunAsync(GitHubClientOptions options)
    {
        var client = await GitHubClient.CreateAsync(options, _loggerFactory);

        // Lists by label rather than by tracking marker, so issues from before the marker existed are discarded too.
        // A discarded issue loses the label, so it's never listed or discarded twice.
        var issueNumbers = await client.ListIssueNumbersByLabelAsync(IssueUtil.ImportLabelName);
        _logger.LogInformation("Found {IssueCount} migrated issue(s) to discard on {Owner}/{Repo}.", issueNumbers.Count, options.Owner, options.Repo);

        for (var index = 0; index < issueNumbers.Count; index++)
        {
            var title = await client.DiscardIssueAsync(issueNumbers[index]);
            _logger.LogInformation("[{Index}/{Total}] Discarded #{IssueNumber} \"{Title}\".", index + 1, issueNumbers.Count, issueNumbers[index], title);
        }

        _logger.LogInformation("Discard complete: discarded {IssueCount} migrated issue(s).", issueNumbers.Count);
    }
}
