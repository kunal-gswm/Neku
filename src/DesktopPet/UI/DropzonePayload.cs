using System.IO;
using System.Windows;
using IDataObject = System.Windows.IDataObject;
using DataFormats = System.Windows.DataFormats;

namespace DesktopPet.UI;

public enum DropPayloadType
{
    Text,
    Files,
    Link
}

/// <summary>
/// Structured, safe representation of dropped content on the companion pet.
/// Never executes files or opens URLs. Never reads file contents automatically.
/// </summary>
public sealed class DropzonePayload
{
    public DropPayloadType Type { get; init; }
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public string PreviewText { get; init; } = "";
    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();
    public string? Url { get; init; }

    public static DropzonePayload? FromDataObject(IDataObject? data)
    {
        if (data == null) return null;

        // 1. Files dropped
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            if (data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                string title = files.Length == 1
                    ? $"File: {Path.GetFileName(files[0])}"
                    : $"{files.Length} Files Dropped";

                string content = string.Join(Environment.NewLine, files);

                string preview = files.Length == 1
                    ? files[0]
                    : $"{files.Length} files:{Environment.NewLine}" + string.Join(Environment.NewLine, files.Take(4).Select(f => "• " + Path.GetFileName(f))) + (files.Length > 4 ? $"\n...and {files.Length - 4} more" : "");

                return new DropzonePayload
                {
                    Type = DropPayloadType.Files,
                    Title = title,
                    Content = content,
                    PreviewText = preview,
                    FilePaths = files.ToList()
                };
            }
        }

        // 2. Text / Link dropped
        string? text = null;
        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            text = data.GetData(DataFormats.UnicodeText) as string;
        }
        else if (data.GetDataPresent(DataFormats.Text))
        {
            text = data.GetData(DataFormats.Text) as string;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            string trimmed = text.Trim();

            // Guard against oversized text payloads
            const int maxTextLength = 65536;
            if (trimmed.Length > maxTextLength)
            {
                trimmed = trimmed[..maxTextLength] + "… (truncated)";
            }

            // Check if string is a Web URL
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                string host = uri.Host;
                string title = string.IsNullOrWhiteSpace(host) ? "Web Link" : $"Link: {host}";

                return new DropzonePayload
                {
                    Type = DropPayloadType.Link,
                    Title = title,
                    Content = trimmed,
                    PreviewText = trimmed,
                    Url = trimmed
                };
            }

            // Normal text
            string firstLine = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Quick Note";
            if (firstLine.Length > 45) firstLine = firstLine[..42] + "...";

            string preview = trimmed.Length > 300 ? trimmed[..297] + "..." : trimmed;

            return new DropzonePayload
            {
                Type = DropPayloadType.Text,
                Title = firstLine,
                Content = trimmed,
                PreviewText = preview
            };
        }

        return null;
    }
}
