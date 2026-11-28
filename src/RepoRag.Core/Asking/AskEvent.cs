using System.Text.Json.Serialization;
using RepoRag.Core.Search;

namespace RepoRag.Core.Asking;

/// <summary>One event in an answer stream (ADR 0007). Serialized with a "type" field the page switches on.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextEvent), "text")]
[JsonDerivedType(typeof(SearchingEvent), "searching")]
[JsonDerivedType(typeof(SourcesEvent), "sources")]
[JsonDerivedType(typeof(DoneEvent), "done")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
public abstract record AskEvent;

/// <summary>A piece of the answer, as the model writes it.</summary>
public sealed record TextEvent(string Delta) : AskEvent;

/// <summary>A query the agent runs during dig deeper, shown live.</summary>
public sealed record SearchingEvent(string Query) : AskEvent;

/// <summary>Cited first, then also searched. Known only when the text is complete.</summary>
public sealed record SourcesEvent(IReadOnlyList<SourceRef> Cited, IReadOnlyList<SourceRef> AlsoSearched) : AskEvent;

public sealed record DoneEvent(string ConversationId, int SearchesUsed) : AskEvent;

public sealed record ErrorEvent(string Message) : AskEvent;

/// <summary>A source as the page shows it.</summary>
public sealed record SourceRef(string Label, string SourcePath, string HeadingPath, string Text)
{
    public static SourceRef From(DocRecord r) => new(r.Label, r.SourcePath, r.HeadingPath, r.Text);
}
