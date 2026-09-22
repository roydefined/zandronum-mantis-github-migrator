using MantisGithubMigrator.Cli.Commands;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

switch (args[0])
{
    case "normalize":
        new NormalizeCommand().Run();
        break;
    case "migrate":
        new MigrateCommand().Run();
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
