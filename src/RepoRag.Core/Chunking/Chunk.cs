namespace RepoRag.Core.Chunking;

/// <summary>One piece of a document, ready to be embedded. Our own type: no parser types leak out.</summary>
/// <param name="Id">Stable id: "relative/path.md#index".</param>
/// <param name="SourcePath">Repo-relative path with forward slashes.</param>
/// <param name="HeadingPath">"Architecture › Commands › Routing", or empty when the file has no headings.</param>
/// <param name="Text">The chunk's Markdown text, without its heading line.</param>
public sealed record Chunk(string Id, string SourcePath, string HeadingPath, string Text);
