using System.Text;
using System.Text.Json;

namespace Chess.Web.Site;

/// <summary>Writes the export's files under one root and counts what it wrote. Paths are relative and use forward slashes.</summary>
internal sealed class SiteOutput(string root)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public int Pages { get; private set; }

    public long Bytes { get; private set; }

    /// <summary>Writes an HTML page. The document type is added here, since a component renders the element tree only.</summary>
    public async Task WritePageAsync(string relativePath, string html, CancellationToken ct)
    {
        await WriteBytesAsync(relativePath, Utf8.GetBytes("<!DOCTYPE html>\n" + html), ct);
        Pages++;
    }

    public Task WriteJsonAsync<T>(string relativePath, T value, CancellationToken ct) =>
        WriteBytesAsync(relativePath, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), ct);

    public Task WriteBytesAsync(string relativePath, byte[] content, CancellationToken ct) =>
        WriteStreamAsync(relativePath, new MemoryStream(content), ct);

    /// <summary>Copies a file the site serves as an asset (its bytes are counted, its page count is not).</summary>
    public Task CopyAsync(string relativePath, Stream source, CancellationToken ct) => WriteStreamAsync(relativePath, source, ct);

    private async Task WriteStreamAsync(string relativePath, Stream content, CancellationToken ct)
    {
        await using var source = content;
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await source.CopyToAsync(file, ct);
        Bytes += file.Length;
    }
}
