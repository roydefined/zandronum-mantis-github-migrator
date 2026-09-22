# zandronum-mantis-github-migrator

A one-off migration tool for moving issue data from the Zandronum MantisBT bug tracker into this project's GitHub repository.

## Projects

- `src/MantisGithubMigrator.Core`: MantisBT export models and the normalize step that turns a raw export into a validated internal representation.
- `src/MantisGithubMigrator.GitHub`: client for creating issues, comments, and attachments on GitHub via the GitHub REST API.
- `src/MantisGithubMigrator.Cli`: command-line entry point tying the steps together.

## Samples

[samples/](samples/) contains a sample MantisBT export (`mantis_export_sample.json`, ~50 issues) plus its referenced attachments, used for development and testing.
