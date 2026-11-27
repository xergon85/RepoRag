using System.Text.Encodings.Web;
using System.Text.Json;
using RepoRag.Core.Chunking;

namespace RepoRag.Core.Indexing;

/// <summary>The files of one index in ~/.reporag/&lt;name&gt;/ (ADR 0002).</summary>
public sealed class IndexStore(string directory)
{
    public const string ConfigFile = "config.json";
    public const string VectorsFile = "vectors.json";

    private static readonly JsonSerializerOptions Readable = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // vectors.json is for the machine: thousands of numbers, so no indentation.
    private static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web);

    public string Directory { get; } = directory;

    /// <summary>The name of this index: the folder's name.</summary>
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Directory));

    /// <summary>~/.reporag/&lt;name&gt;/</summary>
    public static IndexStore ForName(string name)
        => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".reporag", name));

    public async Task<IndexConfig?> ReadConfigAsync(CancellationToken ct = default)
        => await ReadAsync<IndexConfig>(ConfigFile, ct);

    public Task WriteConfigAsync(IndexConfig config, CancellationToken ct = default)
        => WriteAsync(ConfigFile, config, Readable, ct);

    public Task WriteChunksAsync(IReadOnlyList<Chunk> chunks, CancellationToken ct = default)
        => ChunkFileWriter.WriteAsync(Directory, chunks, ct);

    public async Task<IReadOnlyDictionary<string, float[]>> ReadVectorsAsync(CancellationToken ct = default)
        => await ReadAsync<Dictionary<string, float[]>>(VectorsFile, ct) ?? new Dictionary<string, float[]>();

    public Task WriteVectorsAsync(IReadOnlyDictionary<string, float[]> vectors, CancellationToken ct = default)
        => WriteAsync(VectorsFile, vectors, Compact, ct);

    private async Task<T?> ReadAsync<T>(string file, CancellationToken ct) where T : class
    {
        var path = Path.Combine(Directory, file);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Readable, ct);
    }

    private async Task WriteAsync<T>(string file, T value, JsonSerializerOptions options, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        await using var stream = File.Create(Path.Combine(Directory, file));
        await JsonSerializer.SerializeAsync(stream, value, options, ct);
    }
}
