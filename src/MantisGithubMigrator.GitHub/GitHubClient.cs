using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Octokit;
using OctokitClient = Octokit.GitHubClient;

namespace MantisGithubMigrator.GitHub;

public sealed class GitHubClient
{
    private static readonly ProductHeaderValue ProductHeader = new("MantisGithubMigrator");

    // refresh the installation token once it's within this long of expiring
    private static readonly TimeSpan InstallationTokenRefreshMargin = TimeSpan.FromMinutes(2);

    private readonly OctokitClient _client;
    private readonly string _owner;
    private readonly string _repo;

    // set for a GitHub App, null for a plain PAT
    private readonly string? _appId;
    private readonly string? _appPrivateKeyPem;
    private readonly long _installationId;
    private DateTimeOffset _installationTokenExpiresAt = DateTimeOffset.MaxValue;

    private GitHubClient(string owner, string repo, OctokitClient client)
    {
        _owner = owner;
        _repo = repo;
        _client = client;
    }

    private GitHubClient(string owner, string repo, string appId, string appPrivateKeyPem, long installationId)
        : this(owner, repo, new OctokitClient(ProductHeader))
    {
        _appId = appId;
        _appPrivateKeyPem = appPrivateKeyPem;
        _installationId = installationId;
    }

    public static async Task<GitHubClient> CreateAsync(GitHubClientOptions options)
    {
        if (options.AppId is null)
        {
            var client = new OctokitClient(ProductHeader)
            {
                Credentials = new Credentials(options.Token!),
            };
            return new GitHubClient(options.Owner, options.Repo, client);
        }

        var appClient = new OctokitClient(ProductHeader)
        {
            Credentials = new Credentials(BuildAppJwt(options.AppId, options.AppPrivateKey!), AuthenticationType.Bearer),
        };
        var installation = await appClient.GitHubApps.GetRepositoryInstallationForCurrent(options.Owner, options.Repo);

        var result = new GitHubClient(options.Owner, options.Repo, options.AppId, options.AppPrivateKey!, installation.Id);
        await result.RefreshInstallationTokenAsync();
        return result;
    }

    public async Task EnsureLabelsAsync(IEnumerable<LabelDefinition> labels)
    {
        await EnsureFreshTokenAsync();

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
        await EnsureFreshTokenAsync();

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
        await EnsureFreshTokenAsync();
        await _client.Issue.Comment.Create(_owner, _repo, issueNumber, body);
    }

    public async Task<IReadOnlyList<int>> ListIssueNumbersByLabelAsync(string labelName)
    {
        await EnsureFreshTokenAsync();

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
        await EnsureFreshTokenAsync();
        await _client.Issue.Update(_owner, _repo, issueNumber, new IssueUpdate { State = ItemState.Closed });
    }

    private async Task EnsureFreshTokenAsync()
    {
        // PATs don't expire mid-run, nothing to refresh
        if (_appId is null)
            return;

        if (DateTimeOffset.UtcNow < _installationTokenExpiresAt - InstallationTokenRefreshMargin)
            return;

        await RefreshInstallationTokenAsync();
    }

    private async Task RefreshInstallationTokenAsync()
    {
        var appClient = new OctokitClient(ProductHeader)
        {
            Credentials = new Credentials(BuildAppJwt(_appId!, _appPrivateKeyPem!), AuthenticationType.Bearer),
        };

        var token = await appClient.GitHubApps.CreateInstallationToken(_installationId);
        _client.Credentials = new Credentials(token.Token);
        _installationTokenExpiresAt = token.ExpiresAt;
    }

    // GitHub caps app JWTs at 10 minutes and wants iss = app id, RS256-signed. Backdating iat by 60s covers clock drift between this machine and GitHub's.
    private static string BuildAppJwt(string appId, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
        {
            // Always mint a fresh RSA per call and dispose right after, so IdentityModel's signature-provider cache would otherwise hand back one tied to a disposed key.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        var handler = new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false };

        var now = DateTime.UtcNow.AddSeconds(-60);

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = appId,
            IssuedAt = now,
            Expires = now.AddMinutes(10),
            SigningCredentials = signingCredentials,
        });

        return handler.WriteToken(token);
    }
}
