using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace RepoRag.Core.Search;

public sealed record SearchHit(DocRecord Record, double Score);

/// <summary>Embeds a query and finds the closest chunks. Knows nothing about files or databases (ADR 0006).</summary>
public sealed class DocSearch(
    VectorStoreCollection<string, DocRecord> collection,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int top = 5, CancellationToken ct = default)
    {
        var vector = await embedder.GenerateVectorAsync(query, cancellationToken: ct);

        var hits = new List<SearchHit>();
        await foreach (var result in collection.SearchAsync(vector, top, cancellationToken: ct))
            hits.Add(new SearchHit(result.Record, result.Score ?? 0));

        return hits.OrderByDescending(h => h.Score).ToList();
    }
}
