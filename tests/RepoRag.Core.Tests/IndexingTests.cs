using System.Text.Json;
using RepoRag.Core.Chunking;
using RepoRag.Core.Indexing;

namespace RepoRag.Core.Tests;

public sealed class IndexingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("reporag-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact] // ADR 0004: default is docs/** plus root-level .md files
    public void Default_reads_docs_and_root_markdown()
    {
        Write("README.md", "x");
        Write("CONTRIBUTING.md", "x");
        Write("docs/adr/0001.md", "x");
        Write("docs/guide.md", "x");
        Write("src/Notes.md", "x");             // outside the allowlist
        Write("session-ses_42f4.md", "x");      // root .md: allowed (maintainer's call)
        Write("docs/readme.txt", "x");          // not Markdown

        var files = RepoScanner.FindMarkdownFiles(_root);

        Assert.Equal(["CONTRIBUTING.md", "README.md", "docs/adr/0001.md", "docs/guide.md", "session-ses_42f4.md"], files);
    }

    [Fact] // ADR 0004: dot-folders are skipped, even inside allowed folders
    public void Dot_folders_are_skipped()
    {
        Write("docs/guide.md", "x");
        Write("docs/.drafts/wip.md", "x");
        Write(".claude/worktrees/copy/docs/guide.md", "x");

        var files = RepoScanner.FindMarkdownFiles(_root, ["docs", ".claude"]);

        Assert.Equal(["docs/guide.md"], files);
    }

    [Fact] // ADR 0004: a configured allowlist replaces the default; folders and single files both work
    public void Allowlist_replaces_default()
    {
        Write("README.md", "x");
        Write("docs/guide.md", "x");
        Write("adr/0001.md", "x");
        Write("design/notes.md", "x");

        var files = RepoScanner.FindMarkdownFiles(_root, ["adr", "design/notes.md"]);

        Assert.Equal(["adr/0001.md", "design/notes.md"], files);
    }

    [Fact] // ADR 0004: an allowed folder that doesn't exist is fine
    public void Missing_allowed_folder_is_ignored()
    {
        Write("README.md", "x");

        var files = RepoScanner.FindMarkdownFiles(_root);

        Assert.Equal(["README.md"], files);
    }

    [Fact] // ADR 0001: chunks.json is written, and reads back the same
    public async Task Writer_writes_chunks_json()
    {
        var index = Path.Combine(_root, "index");
        Chunk[] chunks = [new("a.md#0", "a.md", "Intro › Setup", "Run it.")];

        await ChunkFileWriter.WriteAsync(index, chunks);

        var json = await File.ReadAllTextAsync(Path.Combine(index, ChunkFileWriter.FileName));
        Assert.Contains("Intro › Setup", json); // readable, not \u-escaped
        var back = JsonSerializer.Deserialize<Chunk[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(chunks, back);
    }
}
