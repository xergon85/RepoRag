using RepoRag.Core.Chunking;
using RepoRag.Core.Embedding;
using RepoRag.Core.Indexing;
using RepoRag.Core.Search;

namespace RepoRag.Core.Tests;

// ADR 0006: opening an index through the file adapter, and searching it.
public sealed class SearchTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("reporag-repo-").FullName;
    private readonly string _indexRoot = Directory.CreateTempSubdirectory("reporag-root-").FullName;
    private readonly FakeEmbeddingGenerator _embedder = new();

    public void Dispose()
    {
        Directory.Delete(_repo, recursive: true);
        Directory.Delete(_indexRoot, recursive: true);
    }

    private async Task Index(string model = "model-a")
    {
        Directory.CreateDirectory(Path.Combine(_repo, "docs"));
        File.WriteAllText(Path.Combine(_repo, "docs", "combat.md"), "# Combat\n\nDamage is rolled from weapon dice and strength.");
        File.WriteAllText(Path.Combine(_repo, "docs", "rooms.md"), "# Rooms\n\nRooms connect through exits north south east west.");
        var indexer = new RepoIndexer(new HeadingChunker(new ChunkingOptions()), new CachedEmbedder(_embedder), model, 200);
        await indexer.IndexAsync(_repo, new IndexStore(Path.Combine(_indexRoot, "test")));
    }

    [Fact]
    public async Task Opens_an_index_and_finds_the_closest_chunk()
    {
        await Index();
        var collection = await new FileIndexSource("model-a", _indexRoot).OpenAsync("test");

        var hits = await new DocSearch(collection, _embedder).SearchAsync("how is weapon damage rolled", top: 1);

        var hit = Assert.Single(hits);
        Assert.Equal("docs/combat.md › Combat", hit.Record.Label);
    }

    [Fact]
    public async Task Search_returns_at_most_top_hits_best_first()
    {
        await Index();
        var collection = await new FileIndexSource("model-a", _indexRoot).OpenAsync("test");

        var hits = await new DocSearch(collection, _embedder).SearchAsync("rooms exits north", top: 5);

        Assert.Equal(2, hits.Count);
        Assert.Equal("docs/rooms.md", hits[0].Record.SourcePath);
        Assert.True(hits[0].Score >= hits[1].Score);
    }

    [Fact] // ADR 0002 / 0006: asking against an index built with another model stops
    public async Task Opening_with_another_embedding_model_stops()
    {
        await Index(model: "model-a");

        await Assert.ThrowsAsync<EmbeddingModelMismatchException>(() => new FileIndexSource("model-b", _indexRoot).OpenAsync("test"));
    }

    [Fact]
    public async Task Opening_a_missing_index_says_so()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => new FileIndexSource("model-a", _indexRoot).OpenAsync("nope"));
        Assert.Contains("nope", e.Message);
    }

    [Fact] // ADR 0006: search code knows nothing about the file format
    public void Search_code_has_no_file_dependency()
    {
        var searchTypes = new[] { typeof(DocSearch), typeof(Asking.Conversation) };
        var forbidden = new[] { typeof(IndexStore), typeof(FileIndexSource) };

        foreach (var type in searchTypes)
        foreach (var ctor in type.GetConstructors())
            Assert.DoesNotContain(ctor.GetParameters(), p => forbidden.Contains(p.ParameterType));
    }
}
