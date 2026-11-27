# 0005. Embedding and caching

Status: accepted (2026-11-27). Builds on ADR 0002 (where the index lives).

## Context
Indexing a repo should be one finished step: scan, chunk, embed, save.
Embedding costs calls and time, so unchanged text shouldn't be embedded again.

## Decision
- RepoRag codes against `IEmbeddingGenerator` from `Microsoft.Extensions.AI`.
  The Agent Framework is already our core dependency and is built on the same
  abstractions, so this adds no new lock-in. Swapping provider (OpenCode,
  Azure OpenAI, Ollama, local ONNX) is a registration change.
- Each vector is stored with a SHA-256 hash of the text that was embedded
  (heading path + chunk text). Re-indexing embeds only text whose hash is new;
  vectors for text that no longer exists are dropped. Not keyed on chunk id,
  because ids shift when a paragraph is added above.
- If the configured embedding model differs from the one that built the index,
  indexing stops and asks for `--rebuild`. No silent re-embedding of a whole repo.
- `config.json` stores the repo path, the allowlist, the word limit and the
  embedding model. A later run without `--include` reuses the stored allowlist.

## Options considered
- Own `IEmbedder` interface: wraps an abstraction that already exists to prevent lock-in.
- Cache per file: more bookkeeping, and one small edit re-embeds the whole file.
- Automatic re-embed on model change: convenient, but can quietly cost many calls.

## Checks (broken when...)
- Re-indexing unchanged docs makes no embedding calls.
- Changing one chunk's text embeds only that chunk.
- Text that was removed has no vector left in `vectors.json`.
- Renaming a heading re-embeds the chunks under it.
- A model change without `--rebuild` stops with a clear message; with it, everything is re-embedded and `config.json` is updated.
- Indexing writes nothing inside the repo (ADR 0002).
- An existing name pointing to another repo path stops instead of overwriting (ADR 0002).
