using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RepoRag.Core.Search;

namespace RepoRag.Core.Asking;

/// <summary>
/// One question and its follow-ups (ADR 0006).
/// Quick answer: our own search, one model call, no tools.
/// Dig deeper: same conversation, a search_docs tool, at most MaxToolSearches.
/// </summary>
public sealed partial class Conversation(IChatClient chatClient, DocSearch search)
{
    public const int QuickTop = 5;
    public const int MaxToolSearches = 3;

    private const string Instructions = """
        You answer questions about a software repository using only the documentation excerpts you are given.
        Each excerpt starts with its source in square brackets, e.g. [docs/guide.md › Setup].
        Cite every claim with that source in square brackets, copied exactly.
        If the excerpts only partly cover the question, answer what they cover and say that digging deeper may help.
        If they don't cover it at all, say you don't know. Never use knowledge from outside the excerpts.
        """;

    private const string DigDeeperPrompt = """
        Dig deeper. Use the search_docs tool to find what is missing from your answer above
        (at most 3 searches, with your own search queries). Then add to your answer:
        write only the new information, with citations. Don't repeat what you already said.
        """;

    private readonly AIAgent _agent = chatClient.AsAIAgent(new ChatClientAgentOptions
    {
        ChatOptions = new ChatOptions { Instructions = Instructions },
    });

    private readonly Dictionary<string, DocRecord> _retrieved = [];  // everything seen, by chunk id, in order
    private readonly List<string> _answers = [];
    private AgentSession? _session;
    private int _toolSearches;
    private readonly ConcurrentQueue<string> _searchesToReport = new(); // filled by the tool, emitted as events
    private Answer? _last;

    public int SearchesUsed => _toolSearches;

    /// <summary>The quick answer: one search on the question, one model call, no tools.</summary>
    public async Task<Answer> AskAsync(string question, CancellationToken ct = default)
    {
        await foreach (var _ in AskStreamingAsync(question, ct)) { }
        return _last!;
    }

    /// <summary>Continues the same conversation with the search tool, and adds to the answer.</summary>
    public async Task<Answer> DigDeeperAsync(CancellationToken ct = default)
    {
        await foreach (var _ in DigDeeperStreamingAsync(ct)) { }
        return _last!;
    }

    /// <summary>The quick answer as a stream of events (ADR 0007).</summary>
    public async IAsyncEnumerable<AskEvent> AskStreamingAsync(string question, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _session = await _agent.CreateSessionAsync(ct);

        var hits = await search.SearchAsync(question, QuickTop, ct);
        Remember(hits);

        var prompt = $"Question: {question}\n\nDocumentation excerpts:\n\n{Format(hits)}";
        await foreach (var e in Stream(prompt, options: null, ct)) yield return e;
    }

    /// <summary>Dig deeper as a stream of events, including the agent's searches (ADR 0007).</summary>
    public async IAsyncEnumerable<AskEvent> DigDeeperStreamingAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_session is null) throw new InvalidOperationException("Ask a question before digging deeper.");

        var tool = AIFunctionFactory.Create(SearchDocsAsync, "search_docs",
            "Search the repository's documentation. Returns the closest excerpts with their sources.");
        var options = new ChatClientAgentRunOptions(new ChatOptions { Tools = [tool] });

        await foreach (var e in Stream(DigDeeperPrompt, options, ct)) yield return e;
    }

    /// <summary>
    /// Runs the agent streaming: text as it comes, the tool's searches as they happen,
    /// and the sources once the text is complete (they can only be checked against the whole text).
    /// </summary>
    private async IAsyncEnumerable<AskEvent> Stream(string prompt, AgentRunOptions? options, [EnumeratorCancellation] CancellationToken ct)
    {
        var text = new StringBuilder();

        await foreach (var update in _agent.RunStreamingAsync(prompt, _session, options, ct))
        {
            while (_searchesToReport.TryDequeue(out var query)) yield return new SearchingEvent(query);

            if (!string.IsNullOrEmpty(update.Text))
            {
                text.Append(update.Text);
                yield return new TextEvent(update.Text);
            }
        }
        while (_searchesToReport.TryDequeue(out var query)) yield return new SearchingEvent(query);

        _last = Record(text.ToString());
        yield return new SourcesEvent(
            _last.Cited.Select(SourceRef.From).ToList(),
            _last.AlsoSearched.Select(SourceRef.From).ToList());
    }

    /// <summary>The tool. It guards its own budget: past the limit it answers "limit reached" instead of searching.</summary>
    private async Task<string> SearchDocsAsync([Description("What to search for, in your own words.")] string query, CancellationToken ct)
    {
        if (_toolSearches >= MaxToolSearches)
            return $"Search limit reached ({MaxToolSearches} searches). Answer with what you have.";

        _toolSearches++;
        _searchesToReport.Enqueue(query);
        var hits = await search.SearchAsync(query, QuickTop, ct);
        Remember(hits);
        return hits.Count > 0 ? Format(hits) : "No matching excerpts.";
    }

    private void Remember(IEnumerable<SearchHit> hits)
    {
        foreach (var hit in hits) _retrieved.TryAdd(hit.Record.Id, hit.Record);
    }

    private static string Format(IEnumerable<SearchHit> hits)
    {
        var text = new StringBuilder();
        foreach (var hit in hits) text.Append($"[{hit.Record.Label}]\n{hit.Record.Text}\n\n");
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Cited first, the rest as "also searched", each source listed once.
    /// A citation must match a retrieved chunk to count (ADR 0006).
    /// </summary>
    private Answer Record(string text)
    {
        _answers.Add(text);

        var citations = _answers
            .SelectMany(a => CitationPattern().Matches(a))
            .SelectMany(m => m.Groups[1].Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var retrieved = _retrieved.Values.ToList();
        var cited = retrieved.Where(r => citations.Any(c => Matches(c, r))).ToList();

        var citedLabels = cited.Select(r => r.Label).ToHashSet();
        return new Answer(
            text,
            DistinctByLabel(cited),
            DistinctByLabel(retrieved.Where(r => !citedLabels.Contains(r.Label))),
            _toolSearches);
    }

    /// <summary>
    /// Models shorten citations, so a citation matches a chunk when the path is the same and its headings
    /// match the end of the chunk's heading path, each one by its start:
    /// "docs/a.md", "docs/a.md › 4.6", "docs/a.md › ... › 4.6" and "docs/a.md › 4.6 Damage Flow"
    /// all match "docs/a.md › Skills › 4.6 Damage Flow".
    /// </summary>
    private static bool Matches(string citation, DocRecord record)
    {
        var parts = citation.Split('›', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != record.SourcePath) return false;

        var cited = parts[1..].Where(h => h is not ("..." or "…")).ToArray(); // "..." means headings were skipped
        var headings = record.HeadingPath.Split('›', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (cited.Length > headings.Length) return false;

        var tail = headings[^cited.Length..];
        return cited.Zip(tail).All(p => p.Second.StartsWith(p.First, StringComparison.OrdinalIgnoreCase));
    }

    private static List<DocRecord> DistinctByLabel(IEnumerable<DocRecord> records)
        => records.DistinctBy(r => r.Label).ToList();

    [GeneratedRegex(@"\[([^\[\]\n]+)\]")]
    private static partial Regex CitationPattern();
}
