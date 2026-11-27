using Microsoft.Extensions.AI;

namespace RepoRag.Core.Tests;

/// <summary>Counts what it was asked to embed; returns a tiny vector derived from the text length.</summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<string> Embedded { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        Embedded.AddRange(list);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
            list.Select(v => new Embedding<float>(new float[] { v.Length, 1f }))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
