using System.Text;
using MantisGithubMigrator.Core.Normalized;

namespace MantisGithubMigrator.GitHub;

public static class IssueUtil
{
    public const string ImportLabelName = "mantis-import";

    private static readonly LabelDefinition ImportLabel =
        new(ImportLabelName, "6E5494", "Applied to every issue migrated from the Zandronum MantisBT tracker.");

    private static readonly Dictionary<IssueStatus, LabelDefinition> StatusLabels = new()
    {
        [IssueStatus.New] = new("status/new", "58A6FF", "Mantis status: new"),
        [IssueStatus.Feedback] = new("status/feedback", "A371F7", "Mantis status: feedback"),
        [IssueStatus.Acknowledged] = new("status/acknowledged", "39C5CF", "Mantis status: acknowledged"),
        [IssueStatus.Confirmed] = new("status/confirmed", "F0883E", "Mantis status: confirmed"),
        [IssueStatus.Assigned] = new("status/assigned", "D4A72C", "Mantis status: assigned"),
        [IssueStatus.Resolved] = new("status/resolved", "3FB950", "Mantis status: resolved"),
        [IssueStatus.Closed] = new("status/closed", "8B949E", "Mantis status: closed"),
    };

    private static readonly Dictionary<IssuePriority, LabelDefinition> PriorityLabels = new()
    {
        [IssuePriority.None] = new("priority/none", "EBEEF1", "Mantis priority: none"),
        [IssuePriority.Low] = new("priority/low", "A5D6A7", "Mantis priority: low"),
        [IssuePriority.Normal] = new("priority/normal", "90CAF9", "Mantis priority: normal"),
        [IssuePriority.High] = new("priority/high", "FFCC80", "Mantis priority: high"),
        [IssuePriority.Urgent] = new("priority/urgent", "EF9A9A", "Mantis priority: urgent"),
        [IssuePriority.Immediate] = new("priority/immediate", "E53935", "Mantis priority: immediate"),
    };

    private static readonly HashSet<IssueStatus> ClosedStatuses = [IssueStatus.Resolved, IssueStatus.Closed];

    public static IReadOnlyList<LabelDefinition> AllLabels =>
        [.. StatusLabels.Values, .. PriorityLabels.Values, ImportLabel];

    public static IReadOnlyList<string> GetLabelNames(NormalizedIssue issue) =>
        [StatusLabels[issue.Status].Name, PriorityLabels[issue.Priority].Name, ImportLabelName];

    public static bool IsClosed(NormalizedIssue issue) => ClosedStatuses.Contains(issue.Status);

    public static string BuildBody(NormalizedIssue issue)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"> Migrated from MantisBT issue #{issue.MantisId}, originally reported by **{issue.Author}** on {FormatTimestamp(issue.CreatedAt)}.");
        sb.AppendLine();
        sb.AppendLine(issue.Description);

        AppendSection(sb, "Steps to Reproduce", issue.StepsToReproduce);
        AppendSection(sb, "Additional Information", issue.AdditionalInformation);

        return sb.ToString().TrimEnd();
    }

    public static string BuildCommentBody(NormalizedComment comment)
    {
        return $"**{comment.Author}** commented on {FormatTimestamp(comment.CreatedAt)}:\n\n{comment.Text}";
    }

    private static void AppendSection(StringBuilder sb, string heading, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        sb.AppendLine();
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        sb.AppendLine(content);
    }

    // The export carries no explicit timezone, so we render the timestamp as-is rather than
    // assert a UTC offset we can't actually confirm.
    private static string FormatTimestamp(DateTimeOffset timestamp) => $"{timestamp:yyyy-MM-dd HH:mm}";
}
