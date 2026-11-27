namespace RepoRag.Core.Indexing;

/// <summary>config.json: what an index was built from and with (ADR 0002, 0004, 0005).</summary>
/// <param name="RepoPath">Absolute path of the indexed repo.</param>
/// <param name="Include">Allowlist; null means the ADR 0004 default (docs/ + root .md).</param>
/// <param name="MaxWords">Chunk size limit (ADR 0003).</param>
/// <param name="EmbeddingModel">The model that made vectors.json.</param>
public sealed record IndexConfig(string RepoPath, IReadOnlyList<string>? Include, int MaxWords, string EmbeddingModel);
