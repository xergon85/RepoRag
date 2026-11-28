using System.Collections.Concurrent;

namespace RepoRag.Core.Asking;

/// <summary>Conversations by id, in memory (ADR 0007). Not tied to a connection, so they survive a reconnect.</summary>
public sealed class ConversationStore(TimeProvider? clock = null)
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromHours(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (Conversation Conversation, DateTimeOffset LastUsed)> _items = new();

    public string Add(Conversation conversation)
    {
        SweepIdle();
        var id = Guid.NewGuid().ToString("N");
        _items[id] = (conversation, _clock.GetUtcNow());
        return id;
    }

    public bool TryGet(string id, out Conversation conversation)
    {
        if (_items.TryGetValue(id, out var item) && _clock.GetUtcNow() - item.LastUsed <= IdleLimit)
        {
            _items[id] = (item.Conversation, _clock.GetUtcNow()); // using it keeps it alive
            conversation = item.Conversation;
            return true;
        }

        conversation = null!;
        return false;
    }

    private void SweepIdle()
    {
        var now = _clock.GetUtcNow();
        foreach (var (id, item) in _items)
            if (now - item.LastUsed > IdleLimit) _items.TryRemove(id, out _);
    }
}
