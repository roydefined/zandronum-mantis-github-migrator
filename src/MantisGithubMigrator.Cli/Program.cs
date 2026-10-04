using MantisGithubMigrator.Cli;
using MantisGithubMigrator.Cli.Commands;
using MantisGithubMigrator.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddConfiguration(configuration.GetSection("Logging"))
    .AddConsole(console => console.FormatterName = CompactConsoleFormatter.Name)
    .AddConsoleFormatter<CompactConsoleFormatter, ConsoleFormatterOptions>());

switch (args[0])
{
    case "normalize":
        new NormalizeCommand(loggerFactory).Run("samples/mantis_export_sample.json", "output/normalized-issues.json");
        break;
    case "migrate":
        var options = GitHubClientOptions.FromConfiguration(configuration);
        await new MigrateCommand(loggerFactory).RunAsync("output/normalized-issues.json", options);
        break;
    case "discard":
        var discardOptions = GitHubClientOptions.FromConfiguration(configuration);
        await new DiscardCommand(loggerFactory).RunAsync(discardOptions);
        break;
    default:
        PrintUsage();
        return 1;
}

return 0;

void PrintUsage()
{
    Console.WriteLine("Usage: migrator <normalize|migrate|discard>");
}
