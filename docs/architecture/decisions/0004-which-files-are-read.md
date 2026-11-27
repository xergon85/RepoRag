# 0004. Which files are read

Status: accepted (2026-11-27). Narrows ADR 0001 ("Markdown only").

## Context
The first run on EliteMUD produced 6,446 chunks, and 81% came from
dot-folders (`.claude/worktrees` held five copies of the repo, plus agent
session logs). Reading "everything except junk" means guessing what junk is.

## Decision
- RepoRag reads only the folders it is allowed to read: an allowlist in
  `config.json`, with folders and/or single files.
- Default when nothing is configured: `docs/` plus the Markdown files in the
  repo root (README, CONTRIBUTING, ...). The files picked are printed on the
  first run.
- Dot-folders are skipped even inside allowed folders, as a cheap safety net.
- What sits inside the allowed folders is the repo maintainer's
  responsibility. RepoRag doesn't try to guess what's junk.
- No `.gitignore` reading and no exclude patterns: no Git dependency, no parser.

## Options considered
- Read everything, skip dot-folders and respect `.gitignore`: catches more
  junk automatically, but needs Git or a parser and still guesses.
- Whole repo minus dot-folders as the default: works on any repo, but pulls
  in everything.
- No default (stop and ask): explicit, but friction on the first run.

## Checks (broken when...)
- With no allowlist, only `docs/**` and root-level `.md` files are read.
- A Markdown file outside the allowed folders is never read.
- A dot-folder inside an allowed folder is skipped.
- A configured allowlist replaces the default.
