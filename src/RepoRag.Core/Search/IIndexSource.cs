using Microsoft.Extensions.VectorData;

namespace RepoRag.Core.Search;

/// <summary>
/// The adapter for where an index lives (ADR 0006). Files now; a database later.
/// Returns a collection that is ready to search.
/// </summary>
public interface IIndexSource
{
    Task<VectorStoreCollection<string, DocRecord>> OpenAsync(string indexName, CancellationToken ct = default);

    /// <summary>The names of the indexes this source can open (ADR 0007).</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}
