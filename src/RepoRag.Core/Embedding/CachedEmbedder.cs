using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using RepoRag.Core.Chunking;

namespace RepoRag.Core.Embedding;

/// <summary>Result of embedding: the vectors by text hash, and how many were reused vs. newly embedded.</summary>
public sealed record EmbeddingResult(IReadOnlyDictionary<string, float[]> VectorsByHash, int Embedded, int Reused);

/// <summary>Embeds only text it hasn't seen before, keyed on a hash of the embedded text (ADR 0005).</summary>
public sealed class CachedEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator)
{
    // Providers cap how many inputs one request may carry; stay well under it.
    private const int BatchSize = 100;

    /// <summary>What actually gets embedded: the heading path gives a small chunk its context.</summary>
    public static string EmbeddedText(Chunk chunk)
        => chunk.HeadingPath.Length > 0 ? $"{chunk.HeadingPath}\n\n{chunk.Text}" : chunk.Text;

    public static string Hash(Chunk chunk)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(EmbeddedText(chunk))));

    /// <summary>
    /// Returns a vector for every chunk's text. Reuses vectors from <paramref name="existing"/> where the hash matches,
    /// embeds the rest, and leaves out hashes no chunk uses any more.
    /// </summary>
    public async Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<Chunk> chunks, IReadOnlyDictionary<string, float[]> existing, CancellationToken ct = default)
    {
        // One entry per distinct text: duplicate text is embedded once.
        var textByHash = new Dictionary<string, string>();
        foreach (var chunk in chunks) textByHash.TryAdd(Hash(chunk), EmbeddedText(chunk));

        var result = new Dictionary<string, float[]>();
        foreach (var hash in textByHash.Keys.Where(existing.ContainsKey))
            result[hash] = existing[hash];

        var missing = textByHash.Where(kv => !existing.ContainsKey(kv.Key)).ToList();
        foreach (var batch in missing.Chunk(BatchSize))
        {
            var embeddings = await generator.GenerateAsync(batch.Select(kv => kv.Value), cancellationToken: ct);
            for (var i = 0; i < batch.Length; i++)
                result[batch[i].Key] = embeddings[i].Vector.ToArray();
        }

        return new EmbeddingResult(result, Embedded: missing.Count, Reused: textByHash.Count - missing.Count);
    }
}
