using System.Text.Json.Serialization;

namespace MantisGithubMigrator.Core.MantisExport;

public sealed class MantisAttachment
{
    [JsonPropertyName("file_id")]
    public int FileId { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = string.Empty;

    [JsonPropertyName("file_type")]
    public string FileType { get; set; } = string.Empty;
}
