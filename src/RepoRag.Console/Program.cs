using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using RepoRag.Core.Asking;
using RepoRag.Core.Chunking;
using RepoRag.Core.Embedding;
using RepoRag.Core.Indexing;
using RepoRag.Core.Providers;
using RepoRag.Core.Search;

// Commands:
//   index <repo-path> [--name <name>] [--include docs,adr,README.md] [--rebuild]
//   ask <name> "<question>"
// Key: user secrets "OpenCode:ApiKey" (Rider: Tools > .NET User Secrets) or env OPENCODE_API_KEY.
// Optional: OpenCode:EmbeddingModel (default text-embedding-3-small), OpenCode:ChatModel (default glm-5.3-flash).

const string usage = """
    Usage:
      index <repo-path> [--name <name>] [--include docs,adr,README.md] [--rebuild]
      ask <name> "<question>"
    """;

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

var embeddingModel = config["OpenCode:EmbeddingModel"] ?? config["OPENCODE_EMBEDDING_MODEL"] ?? "text-embedding-3-small";
var chatModel = config["OpenCode:ChatModel"] ?? config["OPENCODE_MODEL"] ?? "glm-5.3-flash";
var client = OpenCode.CreateClient(apiKey); // one OpenCode session per run
var embedder = client.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();

try
{
    return args switch
    {
        ["index", var repo, ..] => await Index(repo),
        ["ask", var name, var question, ..] => await Ask(name, question),
        _ => Usage(),
    };
}
catch (Exception e) when (e is IndexNameConflictException or EmbeddingModelMismatchException or InvalidOperationException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

async Task<int> Index(string repoArg)
{
    var repoPath = Path.GetFullPath(repoArg);
    if (!Directory.Exists(repoPath))
    {
        Console.Error.WriteLine($"Not a folder: {repoPath}");
        return 1;
    }

    var name = Option("--name") ?? new DirectoryInfo(repoPath).Name;
    IReadOnlyList<string>? include = Option("--include")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var chunking = new ChunkingOptions();
    var indexer = new RepoIndexer(new HeadingChunker(chunking), new CachedEmbedder(embedder), embeddingModel, chunking.MaxWords);
    var store = IndexStore.ForName(name);

    var result = await indexer.IndexAsync(repoPath, store, include, rebuild: args.Contains("--rebuild"));

    Console.WriteLine(include is null ? "Read (stored allowlist or default: docs/ + root .md):" : $"Read ({string.Join(", ", include)}):");
    foreach (var path in result.Paths) Console.WriteLine($"  {path}");
    Console.WriteLine($"Index '{name}' → {store.Directory}");
    Console.WriteLine($"{result.Files} files, {result.Chunks} chunks, {result.Embedded} embedded, {result.Reused} reused");
    return 0;
}

async Task<int> Ask(string name, string question)
{
    var collection = await new FileIndexSource(embeddingModel).OpenAsync(name);
    var conversation = new Conversation(client.GetChatClient(chatModel).AsIChatClient(), new DocSearch(collection, embedder));

    Print(await conversation.AskAsync(question));

    Console.Write("\nDig deeper? (y/N) ");
    if (Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes")
    {
        Console.WriteLine("\n--- Digging deeper ---\n");
        Print(await conversation.DigDeeperAsync());
    }
    return 0;
}

static void Print(Answer answer)
{
    Console.WriteLine(answer.Text);
    Console.WriteLine("\nSources:");
    foreach (var r in answer.Cited) Console.WriteLine($"  [{r.Label}]");
    if (answer.AlsoSearched.Count > 0)
    {
        Console.WriteLine("Also searched:");
        foreach (var r in answer.AlsoSearched) Console.WriteLine($"  {r.Label}");
    }
    if (answer.SearchesUsed > 0) Console.WriteLine($"(searches used: {answer.SearchesUsed}/{Conversation.MaxToolSearches})");
}

int Usage()
{
    Console.Error.WriteLine(usage);
    return 1;
}

string? Option(string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
