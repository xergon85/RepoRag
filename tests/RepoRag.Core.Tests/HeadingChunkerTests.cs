using RepoRag.Core.Chunking;

namespace RepoRag.Core.Tests;

// Checks from ADR 0001 and ADR 0003. All tests use plain strings: no files.
public class HeadingChunkerTests
{
    private static Task<IReadOnlyList<Chunk>> Chunk(string md, int maxWords = 200)
        => new HeadingChunker(new ChunkingOptions(maxWords)).ChunkAsync("docs/test.md", md);

    private static int Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Paragraphs(int count, int wordsEach)
        => string.Join("\n\n", Enumerable.Range(0, count).Select(p =>
            string.Join(' ', Enumerable.Range(0, wordsEach).Select(w => $"word{p}x{w}"))));

    [Fact] // ADR 0001: a file with headings gives one chunk per section
    public async Task File_with_headings_gives_one_chunk_per_section()
    {
        var chunks = await Chunk("# Intro\n\nHello there.\n\n## Setup\n\nRun it.\n\n## Usage\n\nUse it.");

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Hello there.", chunks[0].Text);
        Assert.Equal("Run it.", chunks[1].Text);
    }

    [Fact] // ADR 0001: a 2,000-word file with no headings gives several chunks under the limit
    public async Task Long_file_without_headings_is_split_by_size()
    {
        var chunks = await Chunk(Paragraphs(count: 40, wordsEach: 50)); // 2,000 words

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(Words(c.Text) <= 200, $"{Words(c.Text)} words"));
        Assert.Equal(2000, chunks.Sum(c => Words(c.Text))); // nothing lost
    }

    [Fact] // ADR 0001: a single huge paragraph is split too
    public async Task One_huge_paragraph_is_split_by_words()
    {
        var chunks = await Chunk(Paragraphs(count: 1, wordsEach: 450));

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(Words(c.Text) <= 200));
    }

    [Fact] // ADR 0001: every chunk has a path and a heading path
    public async Task Chunks_carry_source_path_and_heading_path()
    {
        var chunks = await Chunk("# Architecture\n\nTop.\n\n## Commands\n\n### Routing\n\nTable lookup.\n\n## Data\n\nRows.");

        Assert.All(chunks, c => Assert.Equal("docs/test.md", c.SourcePath));
        Assert.Contains(chunks, c => c.HeadingPath == "Architecture › Commands › Routing" && c.Text == "Table lookup.");
        Assert.Contains(chunks, c => c.HeadingPath == "Architecture › Data" && c.Text == "Rows.");
    }

    [Fact] // ADR 0001: heading path is empty only when the file has no headings
    public async Task File_without_headings_has_empty_heading_path()
    {
        var chunks = await Chunk("Just some text.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("", chunk.HeadingPath);
    }

    [Fact] // ADR 0003: a '#' line inside a code block doesn't start a new chunk
    public async Task Hash_inside_code_block_is_not_a_heading()
    {
        var chunks = await Chunk("# Install\n\n```bash\n# install dependencies\nnpm install\n```\n");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Install", chunk.HeadingPath);
        Assert.Contains("# install dependencies", chunk.Text);
    }

    [Fact] // ADR 0003: changing the word limit changes chunk sizes without code changes
    public async Task Word_limit_comes_from_options()
    {
        var md = Paragraphs(count: 10, wordsEach: 50); // 500 words

        var small = await Chunk(md, maxWords: 100);
        var large = await Chunk(md, maxWords: 300);

        Assert.True(small.Count > large.Count);
        Assert.All(small, c => Assert.True(Words(c.Text) <= 100));
    }

    [Fact] // Ids are stable and unique within a file
    public async Task Ids_are_path_plus_index()
    {
        var chunks = await Chunk("# A\n\nOne.\n\n# B\n\nTwo.");

        Assert.Equal(["docs/test.md#0", "docs/test.md#1"], chunks.Select(c => c.Id));
    }

    [Fact] // Empty sections (a heading straight followed by a subheading) produce no empty chunks
    public async Task Empty_sections_are_skipped()
    {
        var chunks = await Chunk("# A\n\n## B\n\nText.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("A › B", chunk.HeadingPath);
    }

    [Fact] // Found on EliteMUD: YAML front matter is not a chunk or a heading
    public async Task Front_matter_is_skipped()
    {
        var chunks = await Chunk("---\nname: doc-keeper\ndescription: keeps docs\n---\n\n# Title\n\nBody.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Title", chunk.HeadingPath);
        Assert.Equal("Body.", chunk.Text);
    }

    [Fact] // Found on EliteMUD: a horizontal rule is never its own chunk
    public async Task Horizontal_rule_is_skipped()
    {
        var chunks = await Chunk("# A\n\nOne.\n\n---\n\n# B\n\nTwo.");

        Assert.Equal(["One.", "Two."], chunks.Select(c => c.Text));
    }

    [Fact] // Found on EliteMUD: a code block is never cut, even when over the limit
    public async Task Big_code_block_stays_whole()
    {
        var code = string.Join('\n', Enumerable.Range(0, 150).Select(i => $"var x{i} = {i};"));
        var chunks = await Chunk($"# Code\n\nIntro.\n\n```csharp\n{code}\n```\n\nAfter.", maxWords: 100);

        var codeChunk = Assert.Single(chunks, c => c.Text.Contains("```"));
        Assert.StartsWith("```csharp", codeChunk.Text);
        Assert.EndsWith("```", codeChunk.Text);
        Assert.All(chunks, c => Assert.Equal(0, c.Text.Split("```").Length % 2 - 1)); // fences always paired
    }

    [Fact] // Found on EliteMUD: a tiny leftover after cutting is merged into the previous piece
    public async Task Tiny_leftover_is_merged()
    {
        var chunks = await Chunk(Paragraphs(count: 1, wordsEach: 410));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(410, chunks.Sum(c => Words(c.Text)));
    }
}
