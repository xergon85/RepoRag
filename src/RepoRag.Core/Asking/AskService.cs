using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using RepoRag.Core.Search;

namespace RepoRag.Core.Asking;

/// <summary>
/// What the hub calls (ADR 0007): list indexes, ask, dig deeper. Streams typed events.
/// Kept in Core so it can be tested without a running server.
/// </summary>
/// <param name="chatClientFor">A chat client per conversation id (OpenCode wants one session per conversation).</param>
public sealed class AskService(
    IIndexSource indexes,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    Func<string, IChatClient> chatClientFor,
    ConversationStore conversations)
{
    public const int MaxQuestionLength = 1000;

    // An opened index stays cached per name, so each question doesn't reload vectors.json.
    private readonly ConcurrentDictionary<string, Lazy<Task<VectorStoreCollection<string, DocRecord>>>> _opened = new();

    public Task<IReadOnlyList<string>> ListIndexesAsync(CancellationToken ct = default) => indexes.ListAsync(ct);

    public async IAsyncEnumerable<AskEvent> AskAsync(
        string index, string question, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > MaxQuestionLength)
        {
            yield return new ErrorEvent($"Ask a question of 1 to {MaxQuestionLength} characters.");
            yield break;
        }

        var (collection, error) = await TryOpenAsync(index);
        if (collection is null)
        {
            yield return new ErrorEvent(error!);
            yield break;
        }

        var id = Guid.NewGuid().ToString("N");
        var conversation = new Conversation(chatClientFor(id), new DocSearch(collection, embedder));
        id = conversations.Add(conversation);

        await foreach (var e in conversation.AskStreamingAsync(question, ct)) yield return e;
        yield return new DoneEvent(id, conversation.SearchesUsed);
    }

    public async IAsyncEnumerable<AskEvent> DigDeeperAsync(
        string conversationId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!conversations.TryGet(conversationId, out var conversation))
        {
            yield return new ErrorEvent("That conversation has expired or doesn't exist. Ask the question again.");
            yield break;
        }

        await foreach (var e in conversation.DigDeeperStreamingAsync(ct)) yield return e;
        yield return new DoneEvent(conversationId, conversation.SearchesUsed);
    }

    /// <summary>Opening can fail (no such index, other embedding model); that becomes an error event, not a crash.</summary>
    private async Task<(VectorStoreCollection<string, DocRecord>? Collection, string? Error)> TryOpenAsync(string index)
    {
        var lazy = _opened.GetOrAdd(index, name => new Lazy<Task<VectorStoreCollection<string, DocRecord>>>(() => indexes.OpenAsync(name)));
        try
        {
            return (await lazy.Value, null);
        }
        catch (Exception e) when (e is InvalidOperationException or Indexing.EmbeddingModelMismatchException)
        {
            _opened.TryRemove(index, out _); // don't cache a failure: the index may be built later
            return (null, e.Message);
        }
    }
}
