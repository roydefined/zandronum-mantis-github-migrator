using Microsoft.Extensions.Configuration;

namespace MantisGithubMigrator.GitHub;

public sealed class GitHubClientOptions
{
    public required string Owner { get; init; }

    public required string Repo { get; init; }

    // Either Token, or AppId + AppPrivateKey - never both, see FromConfiguration.
    public string? Token { get; init; }

    public string? AppId { get; init; }

    public string? AppPrivateKey { get; init; }

    public static GitHubClientOptions FromConfiguration(IConfiguration configuration)
    {
        var token = configuration["GITHUB_TOKEN"];
        var appId = configuration["GITHUB_APP_ID"];
        var appPrivateKey = configuration["GITHUB_APP_PRIVATE_KEY"];

        var hasToken = !string.IsNullOrWhiteSpace(token);
        var hasAppId = !string.IsNullOrWhiteSpace(appId);
        var hasAppPrivateKey = !string.IsNullOrWhiteSpace(appPrivateKey);

        if (hasAppId != hasAppPrivateKey)
        {
            throw new InvalidOperationException(
                "'GITHUB_APP_ID' and 'GITHUB_APP_PRIVATE_KEY' must be set together, or neither.");
        }

        if (hasToken == hasAppId)
        {
            throw new InvalidOperationException(
                "Set exactly one authentication method: either 'GITHUB_TOKEN', or both 'GITHUB_APP_ID' and 'GITHUB_APP_PRIVATE_KEY'.");
        }

        return new GitHubClientOptions
        {
            Owner = Require(configuration, "GITHUB_OWNER"),
            Repo = Require(configuration, "GITHUB_REPO"),
            Token = hasToken ? token : null,
            AppId = hasAppId ? appId : null,
            AppPrivateKey = hasAppId ? appPrivateKey : null,
        };
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Configuration value '{key}' must be set, either as a user secret or an environment variable.");
        }

        return value;
    }
}
