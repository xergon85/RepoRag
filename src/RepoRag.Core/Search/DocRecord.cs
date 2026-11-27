namespace RepoRag.Core.Search;

/// <summary>
/// One searchable chunk in a vector store. The schema is defined in code (see IndexSchema),
/// not with attributes, because the vector size depends on the embedding model (ADR 0006).
/// </summary>
public sealed class DocRecord
{
    public string Id { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string HeadingPath { get; set; } = "";
    public string Text { get; set; } = "";
    public ReadOnlyMemory<float> Vector { get; set; }

    /// <summary>How the record is cited: "docs/x.md › Combat › Damage", or just the path when there are no headings.</summary>
    public string Label => HeadingPath.Length > 0 ? $"{SourcePath} › {HeadingPath}" : SourcePath;
}
