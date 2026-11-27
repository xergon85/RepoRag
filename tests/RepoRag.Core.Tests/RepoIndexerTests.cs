using RepoRag.Core.Chunking;
using RepoRag.Core.Embedding;
using RepoRag.Core.Indexing;

namespace RepoRag.Core.Tests;

// Checks from ADR 0002 and ADR 0005, end to end with temp folders and a fake embedder.
public sealed class RepoIndexerTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("reporag-repo-").FullName;
    private readonly string _otherRepo = Directory.CreateTempSubdirectory("reporag-other-").FullName;
    private readonly IndexStore _store = new(Directory.CreateTempSubdirectory("reporag-index-").FullName);
    private readonly FakeEmbeddingGenerator _generator = new();

    public void Dispose()
    {
        foreach (var dir in new[] { _repo, _otherRepo, _store.Directory }) Directory.Delete(dir, recursive: true);
    }

    private RepoIndexer Indexer(string model = "model-a")
        => new(new HeadingChunker(new ChunkingOptions()), new CachedEmbedder(_generator), model, maxWords: 200);

    private void Doc(string relative, string text)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string[] Snapshot(string dir)
        => Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Order().ToArray();

    [Fact]
    public async Task Writes_config_chunks_and_vectors()
    {
        Doc("README.md", "# Intro\n\nHello.");
        Doc("docs/guide.md", "# Guide\n\nStep one.\n\n# More\n\nStep two.");

        var result = await Indexer().IndexAsync(_repo, _store);

        Assert.Equal((2, 3, 3, 0), (result.Files, result.Chunks, result.Embedded, result.Reused));
        foreach (var file in new[] { IndexStore.ConfigFile, ChunkFileWriter.FileName, IndexStore.VectorsFile })
            Assert.True(File.Exists(Path.Combine(_store.Directory, file)), file);

        var config = await _store.ReadConfigAsync();
        Assert.NotNull(config);
        Assert.Equal(new IndexConfig(Path.GetFullPath(_repo), null, 200, "model-a"), config with { Include = null });
    }

    [Fact] // ADR 0002
    public async Task Indexing_writes_nothing_inside_the_repo()
    {
        Doc("README.md", "# Intro\n\nHello.");
        var before = Snapshot(_repo);

        await Indexer().IndexAsync(_repo, _store);

        Assert.Equal(before, Snapshot(_repo));
    }

    [Fact] // ADR 0002 + 0005: a restart doesn't re-embed anything
    public async Task Second_run_on_unchanged_docs_embeds_nothing()
    {
        Doc("README.md", "# Intro\n\nHello.");
        await Indexer().IndexAsync(_repo, _store);
        _generator.Embedded.Clear();

        var result = await Indexer().IndexAsync(_repo, _store); // a fresh indexer, as after a restart

        Assert.Empty(_generator.Embedded);
        Assert.Equal(1, result.Reused);
    }

    [Fact] // ADR 0002
    public async Task Name_pointing_to_another_repo_stops()
    {
        Doc("README.md", "# Intro\n\nHello.");
        await Indexer().IndexAsync(_repo, _store);

        await Assert.ThrowsAsync<IndexNameConflictException>(() => Indexer().IndexAsync(_otherRepo, _store));
    }

    [Fact] // ADR 0005
    public async Task Model_change_without_rebuild_stops()
    {
        Doc("README.md", "# Intro\n\nHello.");
        await Indexer("model-a").IndexAsync(_repo, _store);

        await Assert.ThrowsAsync<EmbeddingModelMismatchException>(() => Indexer("model-b").IndexAsync(_repo, _store));
    }

    [Fact] // ADR 0005
    public async Task Model_change_with_rebuild_reembeds_everything_and_updates_config()
    {
        Doc("README.md", "# Intro\n\nHello.");
        await Indexer("model-a").IndexAsync(_repo, _store);
        _generator.Embedded.Clear();

        var result = await Indexer("model-b").IndexAsync(_repo, _store, rebuild: true);

        Assert.Equal((1, 0), (result.Embedded, result.Reused));
        Assert.Equal("model-b", (await _store.ReadConfigAsync())!.EmbeddingModel);
    }

    [Fact] // ADR 0005: a later run without --include reuses the stored allowlist
    public async Task Stored_allowlist_is_reused()
    {
        Doc("README.md", "# Intro\n\nHello.");
        Doc("adr/0001.md", "# Decision\n\nWe chose X.");
        await Indexer().IndexAsync(_repo, _store, include: ["adr"]);

        var result = await Indexer().IndexAsync(_repo, _store);

        Assert.Equal(1, result.Files);
        Assert.Equal(["adr"], (await _store.ReadConfigAsync())!.Include);
    }
}
