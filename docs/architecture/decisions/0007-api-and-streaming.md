# 0007. API and streaming

Status: accepted (2026-11-28). Builds on ADR 0006 (asking questions).

## Context
RepoRag needs a web page on top of Core. A dig-deeper turn can take 10-20
seconds, so waiting for a finished answer feels broken. The page should also
show that RepoRag works on any repo, and what the agent does when it digs.

## Decision
- Answers stream end to end: Core, API and page. Core gets streaming
  variants next to its existing methods (the agent's `RunStreamingAsync`).
- The transport is SignalR, using hub streaming (a hub method returns
  `IAsyncEnumerable<AskEvent>`). The traffic is one-way today, but SignalR
  leaves room for server pushes later (e.g. "index out of date"), and it is
  worth learning.
- A conversation has an id and lives in a server-side store, not on the
  SignalR connection, so it survives a reconnect. `Ask(index, question)`
  streams the quick answer; `DigDeeper(conversationId)` continues it.
  In memory for 1.0, the same shape a stored history would have later.
- One stream of typed events per call:
  - `text` (a piece of the answer as it is written)
  - `searching` (a query the agent runs during dig deeper, shown live)
  - `sources` (cited first, then also searched; known only when the text is complete)
  - `done` (conversation id, searches used)
  - `error` (a readable message)
- The page lists the available indexes and you pick one. Listing goes through
  `IIndexSource.ListAsync()`, so a database source can list its indexes too.

## Options considered
- Whole answer at once: simplest, but long silent waits.
- Server-sent events: fits one-way traffic with less machinery, but no room
  for server pushes and nothing new to learn.
- Conversation tied to the connection: simple, but a refresh loses it.
- Text-only stream plus a separate `GetSources(id)` call: two round-trips to coordinate.
- One index set in config: simpler, but doesn't show repo-independence.

## Checks (broken when...)
- A quick answer arrives as several `text` events before `sources` and `done`.
- Each tool search during dig deeper produces a `searching` event with its query, before its results are used.
- `DigDeeper` works with only the conversation id, from a new connection.
- `DigDeeper` with an unknown id gives an `error` event, not a crash.
- The index list contains exactly the indexes that have a `config.json`.
