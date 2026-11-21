---
type: Architecture Decision Record
title: "TIA's cross-reference list is read by a SQLite reader of our own"
description: "XRef.db, TIA's cross-reference list, is a SQLite file; the library reads it with a small read-only reader of the SQLite file format written here, not through a SQLite library, so it keeps taking no dependency."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# TIA's cross-reference list is read by a SQLite reader of our own

TIA keeps its cross-reference list in the archive as `XRef/XRef.db`, a SQLite file of a few small tables, and spec 0015 reads it. `src/TiaFormat` takes no dependency and reads an archive without unpacking it, so the list is read by a read-only reader of the SQLite file format, written here from SQLite's published description of it: the header, the schema table, table b-tree pages, overflow pages and records, and nothing else.

## Considered options

- **A reader of our own** (chosen): a few hundred lines over a format SQLite keeps stable and documents, read from memory like `PEData.plf`; it works with Native AOT on every platform the programs are released for.
- **Microsoft.Data.Sqlite or SQLitePCLRaw**: a full engine, but a native library per platform in a library that takes none, its notices in every release, and a file on disk, so the archive's entry would be unpacked to a temporary file first.
- **Not reading the list**, and improving the references found in the text instead: no new code to keep, but no read or write, no members, no places in LAD, FBD and GRAPH, and nothing about closed blocks, which is what TIA's list has and the text does not.

## Consequences

The reader understands only what TIA writes: no indexes, no write-ahead log, no writing. A list it cannot read counts as absent, and the references come from the text, as for an archive without one. A change in how TIA writes the file shows in the reference projects' tests first.
