using MantisGithubMigrator.Cli.Commands;
using MantisGithubMigrator.GitHub;
using Microsoft.Extensions.Configuration;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

switch (args[0])
{
    case "normalize":
        new NormalizeCommand().Run("samples/mantis_export_sample.json", "output/normalized-issues.json");
        break;
    case "migrate":
        var options = GitHubClientOptions.FromConfiguration(configuration);
        await new MigrateCommand().RunAsync("output/normalized-issues.json", options);
        break;
    case "close":
        new CloseCommand().Run();
        break;
    default:
        PrintUsage();
        return 1;
}

return 0;

void PrintUsage()
{
    Console.WriteLine("Usage: migrator <normalize|migrate|close>");
}
