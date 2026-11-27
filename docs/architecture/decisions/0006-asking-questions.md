# 0006. Asking questions

Status: accepted (2026-11-27)

## Context
With a repo indexed (ADR 0005), RepoRag needs to answer questions about it
with sources. Two forces pull against each other: predictable, testable
retrieval, and an agent that can dig further when a question is vague.

## Decision
- Search goes through `Microsoft.Extensions.VectorData`, with the in-memory
  store for 1.0. Swapping to a database (e.g. pgvector) changes the store
  registration, not the search code. The collection's schema is built from the
  stored vectors' size, so changing embedding model needs no code change.
- Opening an index goes through one adapter, `IIndexSource.OpenAsync(name)`,
  which returns a ready-to-search `VectorStoreCollection`. 1.0 has a
  `FileIndexSource` that loads `chunks.json` + `vectors.json` into the in-memory
  store; a database source (e.g. pgvector) would return its collection directly,
  with no loading step. Search code only ever sees `VectorStoreCollection`.
- The user decides when the agent gets autonomy, not the agent:
  - **Quick answer (default):** one search on the question as asked (top 5
    chunks, put into the prompt), one model call, no tools. Fast, cheap and
    predictable. When the docs only partly cover the question, the answer says
    so, as a hint that digging deeper may help.
  - **Dig deeper (on request):** continues the same conversation. The agent
    knows the question, its first answer and the chunks it already saw, and gets
    a `search_docs` tool to look for what's missing with its own queries, at most
    3 searches. It adds to the current answer instead of replacing it.
- The quick answer's search is our own code: search on the question, then put
  the chunks into the first message. Not `TextSearchProvider`: it searches
  before every model call on the latest message, so "Dig deeper" would trigger
  an extra search on the text "dig deeper" itself. The agent, the conversation
  thread and the tool stay Agent Framework.
- The agent answers only from what it retrieved, cites sources inline as
  `[path › heading]`, and says it doesn't know when the docs don't say.
- The answer shows the cited sources first, then everything else that was
  retrieved under "Also searched". A citation that doesn't match a retrieved
  chunk is never shown as a source.
- Asking against an index built with another embedding model stops (ADR 0002).

## Options considered
- Search only up front: predictable, but a vague question gets a vague search.
- Search only as a tool: more capable, but every run can differ and simple
  questions cost several model calls.
- Search up front plus a tool the agent may always use: the agent decides
  when to spend more, not the user.
- Dig deeper as a fresh run: simpler and stateless, but re-finds what it
  already had and replaces the answer instead of adding to it.
- `TextSearchProvider` for the quick answer: less code, but adds a junk
  search to every dig-deeper turn and hides what the model was given.
- Own cosine-similarity search: about 15 lines and fully transparent, but
  not swappable for a real vector database later.

## Checks (broken when...)
- A quick answer makes exactly one search and one model call, and offers no tools.
- Dig deeper runs in the same conversation as the quick answer and adds to it.
- A 4th tool search during dig deeper is refused with a "limit reached" result.
- A citation that matches no retrieved chunk is not listed as a source.
- Asking against an index with a different embedding model stops with a clear message.
- Search code has no reference to the file format: only `IIndexSource` implementations do.
- A question the docs can't answer gets "I don't know" (checked against the evaluation set).
