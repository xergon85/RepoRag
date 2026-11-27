using RepoRag.Core.Search;

namespace RepoRag.Core.Asking;

/// <param name="Text">This turn's text: the quick answer, or what dig deeper added.</param>
/// <param name="Cited">Sources the conversation's answers cite that match a retrieved chunk.</param>
/// <param name="AlsoSearched">Everything else retrieved so far.</param>
/// <param name="SearchesUsed">Tool searches used in dig-deeper mode.</param>
public sealed record Answer(string Text, IReadOnlyList<DocRecord> Cited, IReadOnlyList<DocRecord> AlsoSearched, int SearchesUsed);
