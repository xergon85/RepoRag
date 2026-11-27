using RepoRag.Core.Chunking;
using RepoRag.Core.Embedding;

namespace RepoRag.Core.Tests;

// Checks from ADR 0005 (caching).
public class CachedEmbedderTests
{
    private readonly FakeEmbeddingGenerator _generator = new();
    private CachedEmbedder Embedder => new(_generator);

    private static Chunk C(string text, string heading = "H") => new("f.md#0", "f.md", heading, text);

    [Fact]
    public async Task Unchanged_text_is_not_reembedded()
    {
        var chunks = new[] { C("one"), C("two") };
        var first = await Embedder.EmbedAsync(chunks, new Dictionary<string, float[]>());
        _generator.Embedded.Clear();

        var second = await Embedder.EmbedAsync(chunks, first.VectorsByHash);

        Assert.Empty(_generator.Embedded);
        Assert.Equal((0, 2), (second.Embedded, second.Reused));
    }

    [Fact]
    public async Task Changed_text_embeds_only_that_chunk()
    {
        var first = await Embedder.EmbedAsync([C("one"), C("two")], new Dictionary<string, float[]>());
        _generator.Embedded.Clear();

        var second = await Embedder.EmbedAsync([C("one"), C("two, edited")], first.VectorsByHash);

        Assert.Equal(["H\n\ntwo, edited"], _generator.Embedded);
        Assert.Equal((1, 1), (second.Embedded, second.Reused));
    }

    [Fact]
    public async Task Removed_text_has_no_vector_left()
    {
        var first = await Embedder.EmbedAsync([C("one"), C("two")], new Dictionary<string, float[]>());

        var second = await Embedder.EmbedAsync([C("one")], first.VectorsByHash);

        Assert.Equal([CachedEmbedder.Hash(C("one"))], second.VectorsByHash.Keys);
    }

    [Fact]
    public async Task Renaming_a_heading_reembeds_its_chunks()
    {
        var first = await Embedder.EmbedAsync([C("text", heading: "Old")], new Dictionary<string, float[]>());
        _generator.Embedded.Clear();

        await Embedder.EmbedAsync([C("text", heading: "New")], first.VectorsByHash);

        Assert.Equal(["New\n\ntext"], _generator.Embedded);
    }

    [Fact]
    public async Task Duplicate_text_is_embedded_once()
    {
        var result = await Embedder.EmbedAsync([C("same"), C("same")], new Dictionary<string, float[]>());

        Assert.Single(_generator.Embedded);
        Assert.Single(result.VectorsByHash);
    }
}
