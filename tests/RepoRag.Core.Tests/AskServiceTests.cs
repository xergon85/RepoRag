using RepoRag.Core.Asking;
using RepoRag.Core.Chunking;
using RepoRag.Core.Embedding;
using RepoRag.Core.Indexing;
using RepoRag.Core.Search;

namespace RepoRag.Core.Tests;

// Checks from ADR 0007: the streamed events, the store and the index list.
public sealed class AskServiceTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("reporag-repo-").FullName;
    private readonly string _indexRoot = Directory.CreateTempSubdirectory("reporag-root-").FullName;
    private readonly FakeEmbeddingGenerator _embedder = new();
    private readonly FakeChatClient _chat = new();

    public void Dispose()
    {
        Directory.Delete(_repo, recursive: true);
        Directory.Delete(_indexRoot, recursive: true);
    }

    private async Task<AskService> Service()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "docs"));
        File.WriteAllText(Path.Combine(_repo, "docs", "combat.md"), "# Combat\n\nDamage is rolled from weapon dice and strength.");
        var indexer = new RepoIndexer(new HeadingChunker(new ChunkingOptions()), new CachedEmbedder(_embedder), "m", 200);
        await indexer.IndexAsync(_repo, new IndexStore(Path.Combine(_indexRoot, "elitemud")));
        return new AskService(new FileIndexSource("m", _indexRoot), _embedder, _ => _chat, new ConversationStore());
    }

    private static async Task<List<AskEvent>> All(IAsyncEnumerable<AskEvent> stream)
    {
        var events = new List<AskEvent>();
        await foreach (var e in stream) events.Add(e);
        return events;
    }

    [Fact] // Several text events, then sources, then done
    public async Task Quick_answer_streams_text_then_sources_then_done()
    {
        var service = await Service();
        _chat.Replies("Damage comes from weapon dice [docs/combat.md › Combat].");

        var events = await All(service.AskAsync("elitemud", "How is damage rolled?"));

        Assert.True(events.OfType<TextEvent>().Count() > 1);
        Assert.Equal("Damage comes from weapon dice [docs/combat.md › Combat].", string.Concat(events.OfType<TextEvent>().Select(t => t.Delta)));
        Assert.IsType<SourcesEvent>(events[^2]);
        Assert.IsType<DoneEvent>(events[^1]);
        Assert.Equal(["docs/combat.md › Combat"], ((SourcesEvent)events[^2]).Cited.Select(s => s.Label));
    }

    [Fact] // Dig deeper works with only the id, and shows each search before what it finds
    public async Task Dig_deeper_by_id_streams_searching_events()
    {
        var service = await Service();
        _chat.Replies("First answer.")
             .CallsTool("search_docs", "strength bonus")
             .Replies("More detail.");

        var first = await All(service.AskAsync("elitemud", "How is damage rolled?"));
        var id = ((DoneEvent)first[^1]).ConversationId;

        var deeper = await All(service.DigDeeperAsync(id)); // nothing but the id: as from a new connection

        var searching = deeper.FindIndex(e => e is SearchingEvent { Query: "strength bonus" });
        var firstText = deeper.FindIndex(e => e is TextEvent);
        Assert.True(searching >= 0, "no searching event");
        Assert.True(searching < firstText, "searching should come before the added text");
        Assert.Equal(1, ((DoneEvent)deeper[^1]).SearchesUsed);
    }

    [Fact]
    public async Task Dig_deeper_with_unknown_id_is_an_error_event()
    {
        var service = await Service();

        var events = await All(service.DigDeeperAsync("nope"));

        Assert.IsType<ErrorEvent>(Assert.Single(events));
    }

    [Fact]
    public async Task Unknown_index_is_an_error_event()
    {
        var service = await Service();

        var events = await All(service.AskAsync("nope", "How is damage rolled?"));

        Assert.IsType<ErrorEvent>(Assert.Single(events));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_question_is_an_error_event(string question)
    {
        var service = await Service();

        var events = await All(service.AskAsync("elitemud", question));

        Assert.IsType<ErrorEvent>(Assert.Single(events));
        Assert.Empty(_chat.Calls);
    }

    [Fact] // The index list is exactly the indexes with a config.json
    public async Task Lists_indexes_that_have_a_config()
    {
        var service = await Service();
        Directory.CreateDirectory(Path.Combine(_indexRoot, "half-done")); // no config.json

        var names = await service.ListIndexesAsync();

        Assert.Equal(["elitemud"], names);
    }

    [Fact]
    public void Store_forgets_idle_conversations()
    {
        var clock = new ManualClock();
        var store = new ConversationStore(clock);
        var id = store.Add(new Conversation(new FakeChatClient(), null!));

        clock.Advance(ConversationStore.IdleLimit + TimeSpan.FromMinutes(1));
        store.Add(new Conversation(new FakeChatClient(), null!)); // adding sweeps old ones

        Assert.False(store.TryGet(id, out _));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
