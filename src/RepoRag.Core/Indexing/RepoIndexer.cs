using RepoRag.Core.Chunking;
using RepoRag.Core.Embedding;

namespace RepoRag.Core.Indexing;

/// <param name="Paths">The files that were read, so the caller can show them (ADR 0004).</param>
public sealed record IndexResult(int Files, int Chunks, int Embedded, int Reused, IReadOnlyList<string> Paths);

/// <summary>Index a repo in one step: scan, chunk, embed, save (ADR 0005).</summary>
public sealed class RepoIndexer(IChunker chunker, CachedEmbedder embedder, string embeddingModel, int maxWords)
{
    /// <param name="include">Allowlist; null reuses the stored one, or the ADR 0004 default on the first run.</param>
    /// <param name="rebuild">Re-embed everything, e.g. after changing embedding model.</param>
    public async Task<IndexResult> IndexAsync(
        string repoPath, IndexStore store, IReadOnlyList<string>? include = null, bool rebuild = false, CancellationToken ct = default)
    {
        var repo = Path.GetFullPath(repoPath);
        var existing = await store.ReadConfigAsync(ct);

        // ADR 0002: a name belongs to one repo.
        if (existing is not null && !SamePath(existing.RepoPath, repo))
            throw new IndexNameConflictException(store.Name, existing.RepoPath, repo);

        // ADR 0005: never re-embed a whole repo without being asked.
        if (existing is not null && existing.EmbeddingModel != embeddingModel && !rebuild)
            throw new EmbeddingModelMismatchException(existing.EmbeddingModel, embeddingModel);

        var allowlist = include ?? existing?.Include;
        var files = RepoScanner.FindMarkdownFiles(repo, allowlist);

        var chunks = new List<Chunk>();
        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(Path.Combine(repo, file), ct);
            chunks.AddRange(await chunker.ChunkAsync(file, content, ct));
        }

        var previous = rebuild ? new Dictionary<string, float[]>() : await store.ReadVectorsAsync(ct);
        var embedded = await embedder.EmbedAsync(chunks, previous, ct);

        await store.WriteChunksAsync(chunks, ct);
        await store.WriteVectorsAsync(embedded.VectorsByHash, ct);
        await store.WriteConfigAsync(new IndexConfig(repo, allowlist, maxWords, embeddingModel), ct); // last: config marks a complete index

        return new IndexResult(files.Count, chunks.Count, embedded.Embedded, embedded.Reused, files);
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
