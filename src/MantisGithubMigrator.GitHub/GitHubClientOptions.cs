using Microsoft.Extensions.Configuration;

namespace MantisGithubMigrator.GitHub;

public sealed class GitHubClientOptions
{
    public required string Owner { get; init; }

    public required string Repo { get; init; }

    public required string Token { get; init; }

    public static GitHubClientOptions FromConfiguration(IConfiguration configuration)
    {
        return new GitHubClientOptions
        {
            Owner = Require(configuration, "GITHUB_OWNER"),
            Repo = Require(configuration, "GITHUB_REPO"),
            Token = Require(configuration, "GITHUB_TOKEN"),
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
