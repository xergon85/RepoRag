using Microsoft.AspNetCore.SignalR;
using RepoRag.Core.Asking;

namespace RepoRag.Api;

/// <summary>
/// The page's one connection (ADR 0007). Kept thin: AskService in Core does the work.
/// Ask and DigDeeper use hub streaming: they return IAsyncEnumerable and the client subscribes.
/// </summary>
public sealed class AskHub(AskService ask) : Hub
{
    public Task<IReadOnlyList<string>> ListIndexes() => ask.ListIndexesAsync(Context.ConnectionAborted);

    public IAsyncEnumerable<AskEvent> Ask(string index, string question, CancellationToken ct)
        => ask.AskAsync(index, question, ct);

    public IAsyncEnumerable<AskEvent> DigDeeper(string conversationId, CancellationToken ct)
        => ask.DigDeeperAsync(conversationId, ct);
}
