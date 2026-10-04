using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Octokit;
using OctokitClient = Octokit.GitHubClient;

namespace MantisGithubMigrator.GitHub;

public sealed class GitHubClient : ICredentialStore
{
    private static readonly ProductHeaderValue ProductHeader = new("MantisGithubMigrator");

    // Refresh the installation token once it's within this long of expiring
    private static readonly TimeSpan InstallationTokenRefreshMargin = TimeSpan.FromMinutes(2);

    // Octokit's default 100 seconds is too short for large attachments.
    private static readonly TimeSpan AssetUploadTimeout = TimeSpan.FromMinutes(30);

    private readonly OctokitClient _client;
    private readonly RateLimiter _rateLimiter;
    private readonly ILogger<GitHubClient> _logger;
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

    private GitHubClient(GitHubClientOptions options, ILoggerFactory loggerFactory, long installationId = 0)
    {
        _owner = options.Owner;
        _repo = options.Repo;
        _token = options.Token;
        _appId = options.AppId;
        _appPrivateKeyPem = options.AppPrivateKey;
        _installationId = installationId;
        _rateLimiter = new RateLimiter(loggerFactory.CreateLogger<RateLimiter>());
        _logger = loggerFactory.CreateLogger<GitHubClient>();
        _client = new OctokitClient(ProductHeader, this);
    }

    public static async Task<GitHubClient> CreateAsync(GitHubClientOptions options, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<GitHubClient>();

        if (options.AppId is null)
        {
            logger.LogInformation("Connecting to {Owner}/{Repo} with a personal access token.", options.Owner, options.Repo);
            return new GitHubClient(options, loggerFactory);
        }

        var appClient = new OctokitClient(ProductHeader)
        {
            Credentials = new Credentials(BuildAppJwt(options.AppId, options.AppPrivateKey!), AuthenticationType.Bearer),
        };
        var installation = await appClient.GitHubApps.GetRepositoryInstallationForCurrent(options.Owner, options.Repo);
        logger.LogInformation("Connecting to {Owner}/{Repo} as GitHub App {AppId} (installation {InstallationId}).",
            options.Owner, options.Repo, options.AppId, installation.Id);

        return new GitHubClient(options, loggerFactory, installation.Id);
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
            _logger.LogInformation("Created label {LabelName}.", label.Name);
        }
    }

    public async Task<int> CreateIssueAsync(string title, string body, IReadOnlyList<string> labelNames)
    {
        var newIssue = new NewIssue(title) { Body = body };
        foreach (var name in labelNames)
            newIssue.Labels.Add(name);

        var issue = await _rateLimiter.RunAsync(() => _client.Issue.Create(_owner, _repo, newIssue), true);
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

    // Lists every issue that carries the import label and a tracking marker, open or closed.
    public async Task<IReadOnlyList<MigratedIssue>> ListMigratedIssuesAsync()
    {
        var request = new RepositoryIssueRequest
        {
            Filter = IssueFilter.All,
            State = ItemStateFilter.All,
        };

        // Searching for this label always yield back the relevant issues as they are always provided with it.
        request.Labels.Add(IssueUtil.ImportLabelName);

        var issues = await _rateLimiter.RunAsync(() => _client.Issue.GetAllForRepository(_owner, _repo, request), false);

        var migratedIssues = new List<MigratedIssue>();
        foreach (var issue in issues)
        {
            if (IssueUtil.GetMantisId(issue.Body) is { } mantisId)
                migratedIssues.Add(new MigratedIssue(issue.Number, mantisId, issue.Comments, issue.State.Value == ItemState.Closed));
        }

        return migratedIssues;
    }

    public async Task CloseIssueAsync(int issueNumber)
    {
        await _rateLimiter.RunAsync(() => _client.Issue.Update(_owner, _repo, issueNumber, new IssueUpdate { State = ItemState.Closed }), true);
    }

    // Issues can't be deleted through the REST API, so a discarded issue is stripped of everything that marks it as migrated instead.
    // Returns the new title.
    public async Task<string> DiscardIssueAsync(int issueNumber)
    {
        var issue = await _rateLimiter.RunAsync(() => _client.Issue.Get(_owner, _repo, issueNumber), false);

        var update = new IssueUpdate
        {
            Title = IssueUtil.BuildDiscardedTitle(issue.Title),
            Body = IssueUtil.RemoveTrackingMarker(issue.Body),
            State = ItemState.Closed,
            StateReason = ItemStateReason.NotPlanned,
        };

        update.ClearLabels();

        await _rateLimiter.RunAsync(() => _client.Issue.Update(_owner, _repo, issueNumber, update), true);
        return update.Title;
    }

    // Ensures releases are ready with their attachments included.
    public async Task<Release> EnsureReleaseAsync(string tag, string body)
    {
        try
        {
            return await _rateLimiter.RunAsync(() => _client.Repository.Release.Get(_owner, _repo, tag), false);
        }
        catch (NotFoundException)
        {
            // Marked as pre-release so it never shows up as the project's latest release.
            var newRelease = new NewRelease(tag)
            {
                Name = tag,
                Body = body,
                Prerelease = true,
            };
            var release = await _rateLimiter.RunAsync(() => _client.Repository.Release.Create(_owner, _repo, newRelease), true);
            _logger.LogInformation("Created release {Tag}.", tag);
            return release;
        }
    }

    // Maps each fully uploaded asset name to its download URL.
    public async Task<Dictionary<string, string>> ListAssetUrlsAsync(Release release)
    {
        var assets = await _rateLimiter.RunAsync(() => _client.Repository.Release.GetAllAssets(_owner, _repo, release.Id), false);

        // An interrupted upload leaves an empty asset behind, which should never be linked to.
        return assets
            .Where(a => a.State == "uploaded")
            .ToDictionary(a => a.Name, a => a.BrowserDownloadUrl);
    }

    // Returns the download URL GitHub gave the asset, so links stay correct even if GitHub changed the name.
    public async Task<string> UploadAssetAsync(Release release, string assetName, string contentType, string path)
    {
        var asset = await _rateLimiter.RunAsync(async () =>
        {
            // Opened per attempt, a retry needs the stream from the start again.
            await using var stream = File.OpenRead(path);
            var upload = new ReleaseAssetUpload(assetName, contentType, stream, AssetUploadTimeout);
            return await _client.Repository.Release.UploadAsset(release, upload);
        }, true);

        return asset.BrowserDownloadUrl;
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
        _logger.LogDebug("Refreshed installation token, valid until {ExpiresAt:HH:mm:ss}.", token.ExpiresAt.ToLocalTime());
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
