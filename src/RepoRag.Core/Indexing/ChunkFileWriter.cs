using System.Text.Encodings.Web;
using System.Text.Json;
using RepoRag.Core.Chunking;

namespace RepoRag.Core.Indexing;

/// <summary>Writes chunks.json into the index folder (ADR 0001, ADR 0002).</summary>
public static class ChunkFileWriter
{
    public const string FileName = "chunks.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,                                  // meant to be opened and read by a person
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "›" and åäö readable
    };

    public static async Task WriteAsync(string indexDirectory, IReadOnlyList<Chunk> chunks, CancellationToken ct = default)
    {
        Directory.CreateDirectory(indexDirectory);
        await using var stream = File.Create(Path.Combine(indexDirectory, FileName));
        await JsonSerializer.SerializeAsync(stream, chunks, Json, ct);
    }
}
