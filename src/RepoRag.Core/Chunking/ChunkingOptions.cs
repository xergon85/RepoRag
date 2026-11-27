namespace RepoRag.Core.Chunking;

/// <summary>Settings that come from config.json. MaxWords: split sections longer than this (ADR 0003).</summary>
public sealed record ChunkingOptions(int MaxWords = 200);
