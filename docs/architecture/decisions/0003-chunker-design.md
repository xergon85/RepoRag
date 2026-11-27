# 0003. Chunker design

Status: accepted (2026-11-27)

## Context
ADR 0001 decides how a repo becomes chunks. This ADR decides how the code
that does it is shaped, so the chunking method can change later
(e.g. running paragraphs through an LLM, or a mix) without rework.

## Decision
- Indexing is three parts, each testable on its own:
  repo scanner (finds Markdown files) → chunker → chunk writer (`chunks.json`).
- The chunker never touches the disk: text in, chunks out.
- One seam: `IChunker`, async from the start so an LLM-based chunker fits
  the same contract.

  `Task<IReadOnlyList<Chunk>> ChunkAsync(string relativePath, string content, CancellationToken ct)`

  `Chunk(Id, SourcePath, HeadingPath, Text)`: our own type, never a Markdig type.
- First implementation: heading-based, using Markdig to read the Markdown
  structure, since we don't want to write our own Markdown parser.
  Markdig stays inside this implementation, so it can be replaced.
- Long sections are split by word count: default ~200 words,
  configurable in `config.json`. Research shows no universal best size;
  ~200 suits factoid questions about docs, and we tune it later against
  an evaluation set.
- Mixed strategies wrap another `IChunker` instead of changing the contract.

## Options considered
- One `Indexer` class doing everything: less code, but every test needs files on disk.
- Own line-based heading detection: breaks on `#` inside code blocks.
- Two seams (a Markdown reader and a chunking strategy): cleaner, but
  speculative before we know what an LLM chunker needs.
- Token-based size: precise, but needs a model-specific tokenizer for no real gain.

## Checks (broken when...)
- The chunker's tests run on plain strings, with no files.
- A `#` line inside a code block doesn't start a new chunk.
- No Markdig type appears outside the heading chunker's implementation.
- Changing the word limit in config changes chunk sizes without code changes.
