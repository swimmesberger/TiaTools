---
type: Architecture Decision Record
title: "Archives that TIA Portal builds from invented sources are committed as reference projects"
description: "Archives that TIA Portal builds from invented sources are committed, with the source TIA generates for them, as reference projects the tests read."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T12:00:00Z }
status: stable
---
# Archives that TIA Portal builds from invented sources are committed as reference projects

The tests read only files our own builder writes, so they confirm what we believe about the format, not what TIA writes; checking against TIA needs TIA Portal, which most contributors and CI do not have. So archives that TIA Portal V21 builds from invented sources, with the source TIA generates for them, are committed under `tests/reference/` (spec 0008). This narrows the rule that no archive enters the repository: the rule exists to keep real projects out, and these hold nothing that was not written here.

## Considered options

- Keep checking by hand on samples: it needs TIA Portal for every check, and stops when no TIA is at hand.
- Commit TIA's generated source only, and build the archives in CI: CI has no TIA Portal, and TIA runs only on a licensed Windows machine.
- Store the archives outside git (Git LFS, a release asset): more moving parts for a few megabytes, and a clone without them would fail the tests.

## Consequences

- Every rebuild adds a new binary archive to the history, about half a megabyte each, so a reference project is built again only when its sources change.
- An archive only ever enters the repository with its sources beside it; the repository check enforces that.
- Only V21 can be built while only V21 is at hand; older versions stay covered by the samples.
