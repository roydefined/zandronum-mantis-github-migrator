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
        var exportPath = Path.Combine(AppContext.BaseDirectory, args.Length > 1 ? args[1] : "samples/mantis_export_sample.json");
        new NormalizeCommand(loggerFactory).Run(exportPath, Path.Combine(AppContext.BaseDirectory, "output/normalized-issues.json"));
        break;
    case "migrate":
        var normalizedPath = Path.Combine(AppContext.BaseDirectory, args.Length > 1 ? args[1] : "output/normalized-issues.json");
        var options = GitHubClientOptions.FromConfiguration(configuration);
        await new MigrateCommand(loggerFactory).RunAsync(normalizedPath, options);
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
    Console.WriteLine("Usage: migrator <normalize [export-file]|migrate [normalized-file]|discard>");
}
