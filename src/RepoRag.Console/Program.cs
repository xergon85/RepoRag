using RepoRag.Core.Chunking;
using RepoRag.Core.Indexing;

// Usage: dotnet run --project src/RepoRag.Console -- index <repo-path> [--name <name>] [--include docs,adr,README.md]
// Writes ~/.reporag/<name>/chunks.json (ADR 0001, 0002). config.json and vectors come in later slices.

if (args is not ["index", var repoArg, ..])
{
    Console.Error.WriteLine("Usage: index <repo-path> [--name <name>] [--include docs,adr,README.md]");
    return 1;
}

var repoPath = Path.GetFullPath(repoArg);
if (!Directory.Exists(repoPath))
{
    Console.Error.WriteLine($"Not a folder: {repoPath}");
    return 1;
}

var nameIndex = Array.IndexOf(args, "--name");
var name = nameIndex >= 0 && nameIndex + 1 < args.Length
    ? args[nameIndex + 1]
    : new DirectoryInfo(repoPath).Name;

var includeIndex = Array.IndexOf(args, "--include");
IReadOnlyList<string>? include = includeIndex >= 0 && includeIndex + 1 < args.Length
    ? args[includeIndex + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : null; // ADR 0004 default: docs/ plus root .md files

var indexDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".reporag", name);
var chunker = new HeadingChunker(new ChunkingOptions());

var chunks = new List<Chunk>();
var files = RepoScanner.FindMarkdownFiles(repoPath, include);
Console.WriteLine(include is null ? "Reading (default: docs/ + root .md):" : $"Reading ({string.Join(", ", include)}):");
foreach (var file in files) Console.WriteLine($"  {file}");

foreach (var file in files)
{
    var content = await File.ReadAllTextAsync(Path.Combine(repoPath, file));
    chunks.AddRange(await chunker.ChunkAsync(file, content));
}

await ChunkFileWriter.WriteAsync(indexDir, chunks);

Console.WriteLine($"{chunks.Count} chunks from {chunks.Select(c => c.SourcePath).Distinct().Count()} files");
Console.WriteLine($"Name '{name}' → {Path.Combine(indexDir, ChunkFileWriter.FileName)}");
return 0;
