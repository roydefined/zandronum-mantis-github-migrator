using System.Text.Json.Serialization;

namespace MantisGithubMigrator.Core.MantisExport;

public sealed class MantisIssue
{
    [JsonPropertyName("mantis_id")]
    public int MantisId { get; set; }

    [JsonPropertyName("project_id")]
    public int ProjectId { get; set; }

    [JsonPropertyName("reporter")]
    public string Reporter { get; set; } = string.Empty;

    [JsonPropertyName("handler")]
    public string Handler { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("steps_to_reproduce")]
    public string StepsToReproduce { get; set; } = string.Empty;

    [JsonPropertyName("additional_information")]
    public string AdditionalInformation { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("attachments")]
    public List<MantisAttachment> Attachments { get; set; } = [];

    [JsonPropertyName("comments")]
    public List<MantisComment> Comments { get; set; } = [];
}
