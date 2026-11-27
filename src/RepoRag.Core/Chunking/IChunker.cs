namespace RepoRag.Core.Chunking;

/// <summary>Turns one file's text into chunks. Never touches the disk (ADR 0003).</summary>
public interface IChunker
{
    Task<IReadOnlyList<Chunk>> ChunkAsync(string relativePath, string content, CancellationToken ct = default);
}
