using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MantisGithubMigrator.Core;
using MantisGithubMigrator.Core.MantisExport;

namespace MantisGithubMigrator.Cli.Commands;

public class NormalizeCommand
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Run(string inputPath, string outputPath)
    {
        var json = File.ReadAllText(inputPath);
        var mantisIssues = JsonSerializer.Deserialize<List<MantisIssue>>(json)
            ?? throw new InvalidDataException($"'{inputPath}' did not contain a valid MantisBT export.");

        var exportDirectory = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        var normalizedIssues = new Normalizer().Normalize(mantisIssues, exportDirectory);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(normalizedIssues, OutputJsonOptions));

        var attachments = normalizedIssues.SelectMany(issue => issue.Attachments).ToList();
        var releaseCount = attachments.Select(attachment => attachment.ReleaseTag).Distinct().Count();
        var totalMegabytes = attachments.Sum(attachment => attachment.SizeBytes) / (1024.0 * 1024.0);

        Console.WriteLine($"Normalized {normalizedIssues.Count} issue(s) from '{inputPath}' to '{outputPath}'.");
        Console.WriteLine($"Attachments: {attachments.Count} file(s), {totalMegabytes:F1} MB, spread over {releaseCount} release(s).");
    }
}
