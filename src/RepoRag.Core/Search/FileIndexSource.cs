using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.VectorData;
using RepoRag.Core.Embedding;
using RepoRag.Core.Indexing;

namespace RepoRag.Core.Search;

/// <summary>Loads ~/.reporag/&lt;name&gt;/ (chunks.json + vectors.json) into an in-memory collection (ADR 0006).</summary>
/// <param name="currentEmbeddingModel">Must match the model the index was built with (ADR 0002).</param>
/// <param name="indexRoot">Folder holding the indexes; defaults to ~/.reporag.</param>
public sealed class FileIndexSource(string currentEmbeddingModel, string? indexRoot = null) : IIndexSource
{
    private readonly string _root = indexRoot
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".reporag");

    public async Task<VectorStoreCollection<string, DocRecord>> OpenAsync(string indexName, CancellationToken ct = default)
    {
        var store = new IndexStore(Path.Combine(_root, indexName));
        var config = await store.ReadConfigAsync(ct)
            ?? throw new InvalidOperationException($"No index named '{indexName}' in {_root}. Run 'index' first.");

        if (config.EmbeddingModel != currentEmbeddingModel)
            throw new EmbeddingModelMismatchException(config.EmbeddingModel, currentEmbeddingModel);

        var chunks = await store.ReadChunksAsync(ct);
        var vectors = await store.ReadVectorsAsync(ct);

        var records = chunks
            .Select(c => (Chunk: c, Found: vectors.TryGetValue(CachedEmbedder.Hash(c), out var v), Vector: v))
            .Where(x => x.Found)
            .Select(x => new DocRecord
            {
                Id = x.Chunk.Id, SourcePath = x.Chunk.SourcePath, HeadingPath = x.Chunk.HeadingPath,
                Text = x.Chunk.Text, Vector = x.Vector,
            })
            .ToList();

        // The schema comes from the stored vectors, so a model with another vector size needs no code change.
        var dimensions = records.Count > 0 ? records[0].Vector.Length : 1;
        var collection = new InMemoryVectorStore().GetCollection<string, DocRecord>(indexName, Schema(dimensions));
        await collection.EnsureCollectionExistsAsync(ct);
        if (records.Count > 0) await collection.UpsertAsync(records, ct);
        return collection;
    }

    /// <summary>Every folder in the index root that holds a config.json, i.e. a complete index (ADR 0005).</summary>
    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> names = Directory.Exists(_root)
            ? Directory.EnumerateDirectories(_root)
                .Where(dir => File.Exists(Path.Combine(dir, IndexStore.ConfigFile)))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
        return Task.FromResult(names);
    }

    private static VectorStoreCollectionDefinition Schema(int dimensions) => new()
    {
        Properties =
        [
            new VectorStoreKeyProperty(nameof(DocRecord.Id), typeof(string)),
            new VectorStoreDataProperty(nameof(DocRecord.SourcePath), typeof(string)),
            new VectorStoreDataProperty(nameof(DocRecord.HeadingPath), typeof(string)),
            new VectorStoreDataProperty(nameof(DocRecord.Text), typeof(string)),
            new VectorStoreVectorProperty(nameof(DocRecord.Vector), typeof(ReadOnlyMemory<float>), dimensions)
            {
                DistanceFunction = DistanceFunction.CosineSimilarity,
            },
        ],
    };
}
