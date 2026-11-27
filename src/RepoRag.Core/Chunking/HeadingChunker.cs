using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace RepoRag.Core.Chunking;

/// <summary>
/// Splits on headings first, then by word count (ADR 0001).
/// Markdig is used only inside this class (ADR 0003).
/// </summary>
public sealed class HeadingChunker(ChunkingOptions options) : IChunker
{
    private const string HeadingSeparator = " › ";

    // Front matter must be recognised, or Markdig reads it as a rule plus a heading.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();

    /// <summary>One block of the section: its original Markdown, and whether it is code (never cut).</summary>
    private readonly record struct Block(string Markdown, bool IsCode);

    public Task<IReadOnlyList<Chunk>> ChunkAsync(string relativePath, string content, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var document = Markdown.Parse(content, Pipeline);
        var headings = new List<(int Level, string Text)>(); // the current heading path
        var sectionBlocks = new List<Block>();               // the blocks under the current heading
        var chunks = new List<Chunk>();

        foreach (var block in document)
        {
            switch (block)
            {
                case YamlFrontMatterBlock or ThematicBreakBlock:
                    continue; // metadata and horizontal rules carry no meaning for search

                case HeadingBlock heading:
                    FlushSection();
                    headings.RemoveAll(h => h.Level >= heading.Level);
                    headings.Add((heading.Level, InlineText(heading.Inline)));
                    break;

                default:
                    // Keep the block's original Markdown, so code blocks and lists stay intact.
                    var markdown = content.Substring(block.Span.Start, block.Span.Length).Trim();
                    if (markdown.Length > 0) sectionBlocks.Add(new Block(markdown, block is CodeBlock));
                    break;
            }
        }

        FlushSection();
        return Task.FromResult<IReadOnlyList<Chunk>>(chunks);

        void FlushSection()
        {
            var headingPath = string.Join(HeadingSeparator, headings.Select(h => h.Text));
            foreach (var text in SplitBySize(sectionBlocks, options.MaxWords))
                chunks.Add(new Chunk($"{relativePath}#{chunks.Count}", relativePath, headingPath, text));
            sectionBlocks.Clear();
        }
    }

    /// <summary>
    /// Packs whole blocks into chunks of up to maxWords.
    /// A code block over the limit stays whole; a prose block over the limit is cut by words.
    /// </summary>
    private static IEnumerable<string> SplitBySize(IReadOnlyList<Block> blocks, int maxWords)
    {
        var current = new List<string>();
        var currentWords = 0;

        foreach (var block in blocks)
        {
            var words = CountWords(block.Markdown);

            if (words > maxWords)
            {
                if (current.Count > 0) { yield return string.Join("\n\n", current); current.Clear(); currentWords = 0; }

                if (block.IsCode) yield return block.Markdown; // a cut code block is unreadable
                else foreach (var piece in CutByWords(block.Markdown, maxWords)) yield return piece;
                continue;
            }

            if (currentWords + words > maxWords && current.Count > 0)
            {
                yield return string.Join("\n\n", current);
                current.Clear();
                currentWords = 0;
            }

            current.Add(block.Markdown);
            currentWords += words;
        }

        if (current.Count > 0) yield return string.Join("\n\n", current);
    }

    /// <summary>Cuts prose into pieces of maxWords. A leftover under a quarter of the limit joins the piece before it.</summary>
    private static IEnumerable<string> CutByWords(string text, int maxWords)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var pieces = words.Chunk(maxWords).Select(p => p.ToList()).ToList();

        if (pieces.Count > 1 && pieces[^1].Count < maxWords / 4)
        {
            pieces[^2].AddRange(pieces[^1]);
            pieces.RemoveAt(pieces.Count - 1);
        }

        return pieces.Select(p => string.Join(' ', p));
    }

    private static int CountWords(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Plain text of a heading, e.g. "Use `dotnet run`" → "Use dotnet run".</summary>
    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null) return "";
        var parts = inline.Descendants().Select(i => i switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => code.Content,
            _ => null
        });
        return string.Concat(parts.Where(p => p is not null)).Trim();
    }
}
