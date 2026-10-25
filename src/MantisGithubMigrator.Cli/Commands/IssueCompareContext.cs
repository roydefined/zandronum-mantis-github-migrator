using MantisGithubMigrator.Core.Normalized;
using MantisGithubMigrator.GitHub;

namespace MantisGithubMigrator.Cli.Commands;

// Represents a context of an issue in relation to a possible existing migrated issue.
public sealed record IssueCompareContext(NormalizedIssue Issue, MigratedIssue? MigratedIssue)
{
    public bool NeedsCreate => MigratedIssue is null;

    // Comments are posted in order, so the ones already on GitHub are the first ones.
    public int PostedCommentCount => Math.Min(MigratedIssue?.CommentCount ?? 0, Issue.Comments.Count);

    public List<NormalizedComment> CommentsToPost => Issue.Comments.Skip(PostedCommentCount).ToList();

    // An issue that should stay open but was closed by hand on GitHub is left alone.
    public bool NeedsClose => IssueUtil.IsClosed(Issue) && MigratedIssue?.IsClosed != true;

    // Attachments are uploaded before an issue is created, so an existing issue already has them.
    public List<NormalizedAttachment> AttachmentsToUpload => NeedsCreate ? Issue.Attachments : [];

    // The issue is done when it exists, has the right status, and all comments were migrated over.
    public bool IsDone => !NeedsCreate && CommentsToPost.Count == 0 && !NeedsClose;
}
