using System.Text.Json.Serialization;

namespace MantisGithubMigrator.Core.MantisExport;

public sealed class MantisComment
{
    [JsonPropertyName("comment_id")]
    public int CommentId { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
