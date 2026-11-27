namespace RepoRag.Core.Indexing;

/// <summary>The name already belongs to an index of another repo (ADR 0002).</summary>
public sealed class IndexNameConflictException(string name, string existingRepo, string requestedRepo)
    : Exception($"The name '{name}' already points to {existingRepo}, not {requestedRepo}. Pick another --name.");

/// <summary>The index was built with another embedding model (ADR 0005).</summary>
public sealed class EmbeddingModelMismatchException(string indexModel, string currentModel)
    : Exception($"This index was built with '{indexModel}', the current model is '{currentModel}'. Run with --rebuild to re-embed everything.");
