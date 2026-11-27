using Microsoft.Extensions.AI;
using RepoRag.Core.Asking;
using RepoRag.Core.Chunking;
using RepoRag.Core.Embedding;
using RepoRag.Core.Indexing;
using RepoRag.Core.Search;

namespace RepoRag.Core.Tests;

// Checks from ADR 0006, with a scripted chat model.
public sealed class ConversationTests : IDisposable
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

    private const string Combat = "docs/combat.md › Combat";
    private const string Rooms = "docs/rooms.md › Rooms";

    private async Task<Conversation> NewConversation(params (string Path, string Text)[] extra)
    {
        Directory.CreateDirectory(Path.Combine(_repo, "docs"));
        foreach (var (path, text) in extra) File.WriteAllText(Path.Combine(_repo, path), text);
        File.WriteAllText(Path.Combine(_repo, "docs", "combat.md"), "# Combat\n\nDamage is rolled from weapon dice and strength.");
        File.WriteAllText(Path.Combine(_repo, "docs", "rooms.md"), "# Rooms\n\nRooms connect through exits north south east west.");
        var indexer = new RepoIndexer(new HeadingChunker(new ChunkingOptions()), new CachedEmbedder(_embedder), "m", 200);
        await indexer.IndexAsync(_repo, new IndexStore(Path.Combine(_indexRoot, "t")));
        var collection = await new FileIndexSource("m", _indexRoot).OpenAsync("t");
        return new Conversation(_chat, new DocSearch(collection, _embedder));
    }

    [Fact] // A quick answer makes exactly one model call and offers no tools
    public async Task Quick_answer_is_one_call_without_tools()
    {
        var conversation = await NewConversation();
        _chat.Replies($"Damage comes from weapon dice [{Combat}].");

        var answer = await conversation.AskAsync("How is damage rolled?");

        var call = Assert.Single(_chat.Calls);
        Assert.Empty(call.ToolNames);
        Assert.Equal($"Damage comes from weapon dice [{Combat}].", answer.Text);
    }

    [Fact] // Every question starts with a search on the question's own text, given to the model
    public async Task Quick_answer_sends_the_question_and_retrieved_chunks()
    {
        var conversation = await NewConversation();
        _chat.Replies("ok");

        await conversation.AskAsync("How is damage rolled?");

        var sent = string.Join("\n", _chat.Calls[0].Messages.Select(m => m.Text));
        Assert.Contains("How is damage rolled?", sent);
        Assert.Contains("Damage is rolled from weapon dice and strength.", sent);
        Assert.Contains($"[{Combat}]", sent);
    }

    [Fact] // Cited first, the rest under "also searched"; made-up citations never count as sources
    public async Task Only_real_citations_count_as_sources()
    {
        var conversation = await NewConversation();
        _chat.Replies($"Weapon dice [{Combat}], see also [docs/made-up.md › Nope].");

        var answer = await conversation.AskAsync("How is damage rolled?");

        Assert.Equal([Combat], answer.Cited.Select(r => r.Label));
        Assert.Contains(Rooms, answer.AlsoSearched.Select(r => r.Label));
        Assert.DoesNotContain(answer.AlsoSearched, r => r.Label == Combat);
    }

    [Fact] // Dig deeper runs in the same conversation, offers the tool, and adds to the answer
    public async Task Dig_deeper_continues_the_same_conversation_with_the_tool()
    {
        var conversation = await NewConversation();
        _chat.Replies("First answer.")
             .CallsTool("search_docs", "strength bonus")
             .Replies("More detail.");

        await conversation.AskAsync("How is damage rolled?");
        var deeper = await conversation.DigDeeperAsync();

        Assert.Equal("More detail.", deeper.Text);
        Assert.Contains("search_docs", _chat.Calls[1].ToolNames);
        Assert.Contains(_chat.Calls[1].Messages, m => m.Text == "First answer."); // it remembers its first answer
        Assert.Equal(1, deeper.SearchesUsed);
    }

    [Fact] // A 4th tool search is refused with "limit reached"
    public async Task Fourth_tool_search_is_refused()
    {
        var conversation = await NewConversation();
        _chat.Replies("First answer.")
             .CallsTool("search_docs", "one").CallsTool("search_docs", "two")
             .CallsTool("search_docs", "three").CallsTool("search_docs", "four")
             .Replies("Done.");

        await conversation.AskAsync("How is damage rolled?");
        var deeper = await conversation.DigDeeperAsync();

        Assert.Equal(Conversation.MaxToolSearches, deeper.SearchesUsed);
        Assert.Contains("limit reached", _chat.ToolResults.Last(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dig_deeper_before_asking_is_an_error()
    {
        var conversation = await NewConversation();

        await Assert.ThrowsAsync<InvalidOperationException>(() => conversation.DigDeeperAsync());
    }

    [Fact] // Found on EliteMUD: models shorten citations (last heading only, a heading's start, path only, two in one bracket)
    public async Task Shortened_citations_still_count()
    {
        var conversation = await NewConversation(
            ("docs/skills.md", "# Skills\n\n## 4.6 Damage Flow\n\nDamage is additive then multiplicative when rolled."));
        _chat.Replies("Additive first [docs/skills.md › 4.6]. Weapon dice [docs/skills.md › 4.6 Damage Flow; docs/combat.md].");

        var answer = await conversation.AskAsync("How is damage rolled?");

        Assert.Equal(["docs/combat.md › Combat", "docs/skills.md › Skills › 4.6 Damage Flow"], answer.Cited.Select(r => r.Label).Order());
    }

    [Fact] // Found on EliteMUD: "..." in a citation stands for skipped headings
    public async Task Ellipsis_in_citation_skips_headings()
    {
        var conversation = await NewConversation(
            ("docs/skills.md", "# Skills\n\n## Combat\n\n### 4.6 Damage Flow\n\nDamage is additive then multiplicative when rolled."));
        _chat.Replies("Additive first [docs/skills.md › ... › 4.6 Damage Flow] and [docs/skills.md › … › 4.6].");

        var answer = await conversation.AskAsync("How is damage rolled?");

        Assert.Contains("docs/skills.md › Skills › Combat › 4.6 Damage Flow", answer.Cited.Select(r => r.Label));
    }

    [Fact] // Shortening never matches a different file or a heading that isn't there
    public async Task Shortened_citation_must_still_match_a_retrieved_chunk()
    {
        var conversation = await NewConversation();
        _chat.Replies("See [docs/combat.md › Defence] and [docs/other.md › Combat].");

        var answer = await conversation.AskAsync("How is damage rolled?");

        Assert.Empty(answer.Cited);
    }

    [Fact] // Found on EliteMUD: a long section split into several chunks is listed once
    public async Task Each_source_is_listed_once()
    {
        var longSection = string.Join(' ', Enumerable.Range(0, 450).Select(i => $"damage{i}")) + " damage rolled";
        var conversation = await NewConversation(("docs/long.md", $"# Long\n\n{longSection}"));
        _chat.Replies("Nothing cited.");

        var answer = await conversation.AskAsync("How is damage rolled?");

        var labels = answer.AlsoSearched.Select(r => r.Label).ToList();
        Assert.Equal(labels.Distinct().Count(), labels.Count);
    }
}
