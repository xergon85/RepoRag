# 0002. Where the index lives

Status: accepted (2026-11-27)

## Context
Vectors cost time and money to create, so a restart shouldn't mean
re-embedding everything. RepoRag reads other people's repos, including
old projects, and shouldn't change them.

## Decision
- Each indexed repo gets a folder in `~/.reporag/<name>/`. Nothing is
  written inside the repo itself, so the repo isn't filled up with extra data.
- The folder holds plain files:
  - `config.json`: repo path, which files to read, and the embedding model used
  - `chunks.json`: the chunks (see ADR 0001)
  - `vectors.json`: one vector per chunk
- The name is just a label. `--name` sets it; without it, the folder name
  of the repo is used. If that name already points to a different repo
  path, RepoRag stops and asks instead of overwriting.
- On startup the vectors are loaded into memory and searched there.
  For 1.0, files are the easiest option with the least lock-in.

## Options considered
- `.reporag/` inside each repo: index next to its source, but adds data to every repo.
- External vector database (e.g. PostgreSQL + pgvector): right for many
  repos or users, too much setup for 1.0. Parked.
- Automatic repo identity (Git remote or path hash): stable, but unreadable
  and more magic than needed.

## Checks (broken when...)
- Indexing a repo writes nothing inside that repo.
- Restarting the app doesn't re-embed anything.
- Searching an index built with a different embedding model than the
  current one stops with a clear message instead of returning results.
- Indexing a second repo under an existing name with a different path stops and asks.
