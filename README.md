# zandronum-mantis-github-migrator

A one-off migration tool for moving issues from the Zandronum MantisBT bug tracker into a GitHub repository.

The tool also supports migrating comments and attachments.

## Using the tool

### Requirements

- [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).
- A build of the tool:
  ```
  dotnet build -c Release src/MantisGithubMigrator.Cli/MantisGithubMigrator.Cli.csproj
  ```
  The tool ends up in `src/MantisGithubMigrator.Cli/bin/Release/net10.0/` as `MantisGithubMigrator.Cli.exe`.

### Commands

| Command | What it does |
|---|---|
| `normalize [export-file] [output-file]` | Converts a MantisBT export into a normalized file. Defaults to `samples/mantis_export_sample.json` and `output/normalized-issues.json`. |
| `migrate [normalized-file]` | Migrates a normalized file to GitHub. Defaults to `output/normalized-issues.json`. |
| `discard` | Discards every issue a migration created. This essentially "resets" the list, and migrating will start over. |

Relative paths are taken from the folder you run the command from.

Always run `normalize` first and `migrate` the result of the normalize. You can adjust the normalized file before migrating, as long as its format stays the same.

Discarding does not delete issues as this is not supported. This is explained more below.

Examples:
```
MantisGithubMigrator.Cli.exe normalize C:\exports\mantis.json C:\exports\normalized.json
MantisGithubMigrator.Cli.exe migrate C:\exports\normalized.json
```

**normalize** checks the export, converts Mantis formatting into GitHub markdown (e.g. ensures any mentions (`@name`) are not accidentally mentioning existing GitHub users), and checks that every attachment exists.

**migrate** works in steps:
1. Checks what's already on GitHub. A migration that was stopped halfway picks up where it left off.
2. Creates the labels for status, priority and `mantis-import` which helps with determining what issues were already migrated.
3. Uploads attachments to `mantis-attachments-N` releases, at most 1000 files per release. Releases are the most consistent place where attachments can be stored.
4. Creates the issues with their comments, linking their attachments, and closes the ones that were closed in Mantis.

GitHub limits how many issues and comments can be created per hour, so a large migration takes hours. The tool waits on its own and can be stopped and started again at any time.

**discard** closes every migrated issue as "not planned", removes its labels and marks its title as `[Discarded]`. GitHub doesn't allow deleting issues, so this is the way to start over. Releases and their attachments are kept and reused.

Because of this limitation in discarding it is highly recommended you only discard in a test environment, unless you're okay with having numerous discarded issues existing in the repository.

### Configuration

The tool needs to know which repository to use and how to log in. Set the following environment variables:

| Variable | |
|---|---|
| `GITHUB_OWNER` | The user or organization that owns the repository. |
| `GITHUB_REPO` | The name of the repository. |
| `GITHUB_APP_ID` | The ID of the GitHub App that runs the migration. |
| `GITHUB_APP_PRIVATE_KEY` | The private key of that app, the full contents of its `.pem` file. |

The app must be installed on the repository with **Issues** and **Contents** set to read and write. Everything it creates shows up under the app's name.

Instead of an app you can use a personal access token with the same permissions, by setting `GITHUB_TOKEN` instead of the two app variables. Everything is then created under your own account. This is simpler than setting up an app.

To avoid flooding your own account with a bunch of issues it is recommended you use an app.

In PowerShell:
```powershell
$env:GITHUB_OWNER = "your-name"
$env:GITHUB_REPO = "your-repository"
```

When using an app:
```powershell
$env:GITHUB_APP_ID = "123456"
$env:GITHUB_APP_PRIVATE_KEY = Get-Content -Raw path\to\key.pem
```

When using a personal account:
```powershell
$env:GITHUB_TOKEN = "123456"
```

Logging is supported, set `Logging__LogLevel__Default` to `Debug` for the most optimal logging.

```powershell
$env:Logging__LogLevel__Default = "Debug"
```

## Development

### Projects

- `src/MantisGithubMigrator.Core`: the MantisBT export format and the normalize step.
- `src/MantisGithubMigrator.GitHub`: the GitHub client, including rate limiting.
- `src/MantisGithubMigrator.Cli`: the commands.
- `tests/MantisGithubMigrator.Core.Tests`: unit tests, run them with `dotnet test MantisGithubMigrator.slnx`.

### Running locally

Instead of environment variables, the configuration can be stored as user secrets:

```
dotnet user-secrets set GITHUB_OWNER your-name --project src/MantisGithubMigrator.Cli
dotnet user-secrets set GITHUB_APP_PRIVATE_KEY (Get-Content -Raw path\to\key.pem) --project src/MantisGithubMigrator.Cli
```

Visual Studio has a launch profile for every command, and VS Code has a matching `run:` task for each. Visual Studio runs from the build folder and VS Code from the repository root, but in both `normalize` writes the file `migrate` reads.

### Samples

[samples/](samples/) is also copied next to the executable on build:

- `mantis_export_sample.json`: around 50 real issues plus their attachments.
- `mantis_export_formatting.json`: a few made-up issues covering every formatting case, to check the markdown conversion.
