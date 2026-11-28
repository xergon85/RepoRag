using Microsoft.Extensions.AI;
using RepoRag.Api;
using RepoRag.Core.Asking;
using RepoRag.Core.Providers;
using RepoRag.Core.Search;

var builder = WebApplication.CreateBuilder(args);

// User secrets are loaded automatically in Development (same id as RepoRag.Console).
var config = builder.Configuration;
var apiKey = config["OpenCode:ApiKey"] ?? config["OPENCODE_API_KEY"]
    ?? throw new InvalidOperationException("Set OpenCode:ApiKey in user secrets, or OPENCODE_API_KEY.");
var embeddingModel = config["OpenCode:EmbeddingModel"] ?? config["OPENCODE_EMBEDDING_MODEL"] ?? "text-embedding-3-small";
var chatModel = config["OpenCode:ChatModel"] ?? config["OPENCODE_MODEL"] ?? "glm-5.3-flash";

var embedder = OpenCode.CreateClient(apiKey).GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();

builder.Services.AddSignalR();
builder.Services.AddSingleton(new AskService(
    new FileIndexSource(embeddingModel),
    embedder,
    conversationId => OpenCode.CreateClient(apiKey, conversationId).GetChatClient(chatModel).AsIChatClient(), // one OpenCode session per conversation
    new ConversationStore()));

var app = builder.Build();

app.MapHub<AskHub>("/hubs/ask");
app.MapGet("/health", () => Results.Ok("ok"));

app.Run();
