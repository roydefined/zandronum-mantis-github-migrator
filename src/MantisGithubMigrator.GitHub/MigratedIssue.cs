namespace MantisGithubMigrator.GitHub;

public sealed record MigratedIssue(int Number, int MantisId, int CommentCount, bool IsClosed);
