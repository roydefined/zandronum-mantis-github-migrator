using Octokit;
using OctokitClient = Octokit.GitHubClient;

namespace MantisGithubMigrator.GitHub;

public sealed class GitHubClient
{
    private readonly OctokitClient _client;
    private readonly string _owner;
    private readonly string _repo;

    public GitHubClient(GitHubClientOptions options)
    {
        _owner = options.Owner;
        _repo = options.Repo;

        _client = new OctokitClient(new ProductHeaderValue("MantisGithubMigrator"))
        {
            Credentials = new Credentials(options.Token),
        };
    }

    public async Task EnsureLabelsAsync(IEnumerable<LabelDefinition> labels)
    {
        var existing = await _client.Issue.Labels.GetAllForRepository(_owner, _repo);
        var existingNames = existing.Select(l => l.Name).ToHashSet();

        foreach (var label in labels)
        {
            if (existingNames.Contains(label.Name))
                continue;

            await _client.Issue.Labels.Create(_owner, _repo, new NewLabel(label.Name, label.Color)
            {
                Description = label.Description,
            });
        }
    }

    public async Task<int> CreateIssueAsync(string title, string body, IReadOnlyList<string> labelNames, bool closed)
    {
        var newIssue = new NewIssue(title) { Body = body };
        foreach (var name in labelNames)
            newIssue.Labels.Add(name);

        var issue = await _client.Issue.Create(_owner, _repo, newIssue);

        if (closed)
            await _client.Issue.Update(_owner, _repo, issue.Number, new IssueUpdate { State = ItemState.Closed });

        return issue.Number;
    }

    public async Task CreateCommentAsync(int issueNumber, string body)
    {
        await _client.Issue.Comment.Create(_owner, _repo, issueNumber, body);
    }

    public async Task<IReadOnlyList<int>> ListIssueNumbersByLabelAsync(string labelName)
    {
        var request = new RepositoryIssueRequest
        {
            Filter = IssueFilter.All,
            State = ItemStateFilter.All,
        };
        request.Labels.Add(labelName);

        var issues = await _client.Issue.GetAllForRepository(_owner, _repo, request);
        return issues.Select(i => i.Number).ToList();
    }

    public async Task CloseIssueAsync(int issueNumber)
    {
        await _client.Issue.Update(_owner, _repo, issueNumber, new IssueUpdate { State = ItemState.Closed });
    }
}
