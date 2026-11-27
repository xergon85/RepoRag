# RepoRag

Ask questions about any repository's docs and ADRs, and get answers with the passages they came from.

Status: work in progress.

## Projects

- `src/RepoRag.Core` - chunking, ingest, search (no web, no UI)
- `src/RepoRag.Console` - test bench
- `src/RepoRag.Api` - minimal API over Core
- `web/` - Angular app (coming)

## Run

```sh
cp .env.example .env   # then add your key
set -a; source .env; set +a
dotnet run --project src/RepoRag.Console
```
