using Microsoft.Extensions.AI;

namespace RepoRag.Core.Tests;

/// <summary>A scripted chat model: each call takes the next reply from the script and records what it was sent.</summary>
public sealed class FakeChatClient : IChatClient
{
    public sealed record Call(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<string> ToolNames);

    private readonly Queue<ChatResponse> _script = new();
    public List<Call> Calls { get; } = [];

    public FakeChatClient Replies(string text) { _script.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))); return this; }

    public FakeChatClient CallsTool(string name, string query)
    {
        var call = new FunctionCallContent(Guid.NewGuid().ToString("N"), name, new Dictionary<string, object?> { ["query"] = query });
        _script.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call(messages.ToList(), options?.Tools?.Select(t => t.Name).ToList() ?? []));
        return Task.FromResult(_script.Dequeue());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        // Plain text replies stream word by word, like a real model; tool calls come as one update.
        if (response.Messages is [{ Contents: [TextContent text] }])
        {
            foreach (var piece in System.Text.RegularExpressions.Regex.Split(text.Text, @"(?<=\s)"))
                if (piece.Length > 0) yield return new ChatResponseUpdate(ChatRole.Assistant, piece);
            yield break;
        }

        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }

    /// <summary>All tool results the model has been sent so far, in order.</summary>
    public IEnumerable<string> ToolResults
        => Calls.LastOrDefault()?.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString() ?? "") ?? [];
}
