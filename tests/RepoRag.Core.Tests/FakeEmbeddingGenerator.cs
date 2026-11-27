using Microsoft.Extensions.AI;

namespace RepoRag.Core.Tests;

/// <summary>
/// Counts what it was asked to embed. Vectors are a tiny bag of words (32 buckets),
/// so texts that share words really are closer: enough to test search without a network.
/// </summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 32;
    public List<string> Embedded { get; } = [];

    public static float[] Vectorize(string text)
    {
        var v = new float[Dimensions];
        foreach (var word in text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var w = new string(word.Where(char.IsLetterOrDigit).ToArray());
            if (w.Length > 0) v[(w.GetHashCode(StringComparison.Ordinal) & 0x7fffffff) % Dimensions] += 1f;
        }
        return v;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        Embedded.AddRange(list);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(Vectorize(v)))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
