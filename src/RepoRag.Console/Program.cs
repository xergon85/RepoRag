using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using RepoRag.Core.Chunking;
using RepoRag.Core.Embedding;
using RepoRag.Core.Indexing;
using RepoRag.Core.Providers;


// Usage: dotnet run --project src/RepoRag.Console -- index <repo-path> [--name <name>] [--include docs,adr,README.md] [--rebuild]
// Needs an OpenCode key: user secrets "OpenCode:ApiKey" (Rider: Tools > .NET User Secrets) or env OPENCODE_API_KEY.
// Optional: "OpenCode:EmbeddingModel" / OPENCODE_EMBEDDING_MODEL (default text-embedding-3-small).

const string usage = "Usage: index <repo-path> [--name <name>] [--include docs,adr,README.md] [--rebuild]";

if (args is not ["index", var repoArg, ..])
{
    Console.Error.WriteLine(usage);
    return 1;
}

var repoPath = Path.GetFullPath(repoArg);
if (!Directory.Exists(repoPath))
{
    Console.Error.WriteLine($"Not a folder: {repoPath}");
    return 1;
}
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var apiKey = config["OpenCode:ApiKey"] ?? config["OPENCODE_API_KEY"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set OpenCode:ApiKey in user secrets, or OPENCODE_API_KEY (see .env.example).");
    return 1;
}

var name = Option("--name") ?? new DirectoryInfo(repoPath).Name;
IReadOnlyList<string>? include = Option("--include")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var rebuild = args.Contains("--rebuild");
var embeddingModel = config["OpenCode:EmbeddingModel"] ?? config["OPENCODE_EMBEDDING_MODEL"] ?? "text-embedding-3-small";
var chunking = new ChunkingOptions();

var generator = OpenCode.CreateClient(apiKey).GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();
var indexer = new RepoIndexer(new HeadingChunker(chunking), new CachedEmbedder(generator), embeddingModel, chunking.MaxWords);
var store = IndexStore.ForName(name);

try
{
    var result = await indexer.IndexAsync(repoPath, store, include, rebuild);
    Console.WriteLine(include is null ? "Read (stored allowlist or default: docs/ + root .md):" : $"Read ({string.Join(", ", include)}):");
    foreach (var path in result.Paths) Console.WriteLine($"  {path}");
    Console.WriteLine($"Index '{name}' → {store.Directory}");
    Console.WriteLine($"{result.Files} files, {result.Chunks} chunks, {result.Embedded} embedded, {result.Reused} reused");
    return 0;
}
catch (Exception e) when (e is IndexNameConflictException or EmbeddingModelMismatchException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

string? Option(string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
