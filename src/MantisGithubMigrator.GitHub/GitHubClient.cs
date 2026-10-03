using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Octokit;
using OctokitClient = Octokit.GitHubClient;

namespace MantisGithubMigrator.GitHub;

public sealed class GitHubClient : ICredentialStore
{
    private static readonly ProductHeaderValue ProductHeader = new("MantisGithubMigrator");

    // Refresh the installation token once it's within this long of expiring
    private static readonly TimeSpan InstallationTokenRefreshMargin = TimeSpan.FromMinutes(2);

    private readonly OctokitClient _client;
    private readonly RateLimiter _rateLimiter;
    private readonly string _owner;
    private readonly string _repo;

    // Set for a plain PAT, null for a GitHub App
    private readonly string? _token;

    // Set for a GitHub App, null for a plain PAT
    private readonly string? _appId;
    private readonly string? _appPrivateKeyPem;
    private readonly long _installationId;
    private Credentials? _installationCredentials;
    private DateTimeOffset _installationTokenExpiresAt;

    private GitHubClient(GitHubClientOptions options, RateLimiter rateLimiter, long installationId = 0)
    {
        _owner = options.Owner;
        _repo = options.Repo;
        _token = options.Token;
        _appId = options.AppId;
        _appPrivateKeyPem = options.AppPrivateKey;
        _installationId = installationId;
        _rateLimiter = rateLimiter;
        _client = new OctokitClient(ProductHeader, this);
    }

    public static async Task<GitHubClient> CreateAsync(GitHubClientOptions options, Action<string> log)
    {
        var rateLimiter = new RateLimiter(log);

        if (options.AppId is null)
            return new GitHubClient(options, rateLimiter);

        var appClient = new OctokitClient(ProductHeader)
        {
            Credentials = new Credentials(BuildAppJwt(options.AppId, options.AppPrivateKey!), AuthenticationType.Bearer),
        };
        var installation = await appClient.GitHubApps.GetRepositoryInstallationForCurrent(options.Owner, options.Repo);

        return new GitHubClient(options, rateLimiter, installation.Id);
    }

    public async Task EnsureLabelsAsync(IEnumerable<LabelDefinition> labels)
    {
        var existing = await _rateLimiter.RunAsync(() => _client.Issue.Labels.GetAllForRepository(_owner, _repo), false);
        var existingNames = existing.Select(l => l.Name).ToHashSet();

        foreach (var label in labels)
        {
            if (existingNames.Contains(label.Name))
                continue;

            var newLabel = new NewLabel(label.Name, label.Color)
            {
                Description = label.Description,
            };
            await _rateLimiter.RunAsync(() => _client.Issue.Labels.Create(_owner, _repo, newLabel), true);
        }
    }

    public async Task<int> CreateIssueAsync(string title, string body, IReadOnlyList<string> labelNames, bool closed)
    {
        var newIssue = new NewIssue(title) { Body = body };
        foreach (var name in labelNames)
            newIssue.Labels.Add(name);

        var issue = await _rateLimiter.RunAsync(() => _client.Issue.Create(_owner, _repo, newIssue), true);

        if (closed)
            await _rateLimiter.RunAsync(() => _client.Issue.Update(_owner, _repo, issue.Number, new IssueUpdate { State = ItemState.Closed }), true);

        return issue.Number;
    }

    public async Task CreateCommentAsync(int issueNumber, string body)
    {
        await _rateLimiter.RunAsync(() => _client.Issue.Comment.Create(_owner, _repo, issueNumber, body), true);
    }

    public async Task<IReadOnlyList<int>> ListIssueNumbersByLabelAsync(string labelName)
    {
        var request = new RepositoryIssueRequest
        {
            Filter = IssueFilter.All,
            State = ItemStateFilter.All,
        };
        request.Labels.Add(labelName);

        var issues = await _rateLimiter.RunAsync(() => _client.Issue.GetAllForRepository(_owner, _repo, request), false);
        return issues.Select(i => i.Number).ToList();
    }

    public async Task CloseIssueAsync(int issueNumber)
    {
        await _rateLimiter.RunAsync(() => _client.Issue.Update(_owner, _repo, issueNumber, new IssueUpdate { State = ItemState.Closed }), true);
    }

    // Octokit asks for credentials on every request, so an installation token that expired during a long rate limit wait gets refreshed here
    async Task<Credentials> ICredentialStore.GetCredentials()
    {
        // PATs don't expire mid-run, nothing to refresh
        if (_appId is null)
            return new Credentials(_token);

        if (_installationCredentials is not null && DateTimeOffset.UtcNow < _installationTokenExpiresAt - InstallationTokenRefreshMargin)
            return _installationCredentials;

        var appClient = new OctokitClient(ProductHeader)
        {
            Credentials = new Credentials(BuildAppJwt(_appId, _appPrivateKeyPem!), AuthenticationType.Bearer),
        };

        var token = await appClient.GitHubApps.CreateInstallationToken(_installationId);
        _installationCredentials = new Credentials(token.Token);
        _installationTokenExpiresAt = token.ExpiresAt;
        return _installationCredentials;
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
