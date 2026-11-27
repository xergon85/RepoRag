namespace RepoRag.Core.Indexing;

/// <summary>Finds the Markdown files RepoRag is allowed to read (ADR 0001, ADR 0004).</summary>
public static class RepoScanner
{
    /// <summary>
    /// Repo-relative paths with forward slashes, sorted, so ids are stable across runs and OSes.
    /// </summary>
    /// <param name="include">
    /// Allowlist of folders and/or files, relative to the repo root.
    /// Null means the default: <c>docs/</c> plus the Markdown files in the repo root.
    /// </param>
    public static IReadOnlyList<string> FindMarkdownFiles(string repoRoot, IReadOnlyList<string>? include = null)
    {
        var root = Path.GetFullPath(repoRoot);

        var files = include is null
            ? Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly).Concat(FilesUnder(Path.Combine(root, "docs")))
            : include.SelectMany(entry => FilesUnder(Path.Combine(root, entry)));

        return files
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(relative => !IsInDotFolder(relative))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A Markdown file itself, every Markdown file under a folder, or nothing if the path doesn't exist.</summary>
    private static IEnumerable<string> FilesUnder(string path)
    {
        if (File.Exists(path))
            return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? [path] : [];

        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.md", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>Dot-folders are skipped even inside allowed folders (ADR 0004).</summary>
    private static bool IsInDotFolder(string relativePath)
        => relativePath.Split('/').SkipLast(1).Any(folder => folder.StartsWith('.'));
}
