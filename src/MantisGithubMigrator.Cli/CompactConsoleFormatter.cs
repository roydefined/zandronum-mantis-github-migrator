using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace MantisGithubMigrator.Cli;

public sealed class CompactConsoleFormatter() : ConsoleFormatter(Name)
{
    public new const string Name = "compact";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var level = logEntry.LogLevel switch
        {
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "trce",
        };

        textWriter.WriteLine($"{DateTime.Now:HH:mm:ss} {level} {logEntry.Formatter(logEntry.State, logEntry.Exception)}");

        if (logEntry.Exception is not null)
            textWriter.WriteLine(logEntry.Exception);
    }
}
