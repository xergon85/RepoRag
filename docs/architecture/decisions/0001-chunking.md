# 0001. How a repository becomes chunks

Status: accepted (2026-11-27)

## Context
RepoRag should work on any repository, including older ones whose docs
follow no particular format.

## Decision
- Only Markdown files are read. We don't want to write a parser that
  extracts information from code, and it keeps 1.0 small. A tool with a
  living-documentation angle may cover code later, but not in 1.0.
- Files are split on headings first, because a heading usually marks one
  topic. Sections that are too long, and files without headings, are split
  by size so unstructured repos still work.
- Each chunk carries its file path and heading path
  (e.g. `Architecture › Commands › Routing`), so small chunks keep their context.
- Chunks are written to `chunks.json` before embedding. That file is where
  an AI preparation step can plug in later. It also makes caching possible:
  files that haven't changed don't need to be re-embedded.

## Options considered
- Fixed-size chunks only: simple, but cuts topics in half in well-structured docs.
- Headings only: breaks down on long or heading-less files.
- Parsing code: out of scope for 1.0.

## Checks (broken when...)
- A Markdown file with headings produces one chunk per section.
- A 2,000-word file with no headings produces several chunks, each under the size limit.
- Every chunk has a file path and a heading path (empty only when the file has no headings).
- No embedding runs without `chunks.json` being written first.
