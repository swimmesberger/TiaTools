---
type: Specification
title: "Spec 0008: reference projects built by TIA Portal"
description: "Small projects that TIA Portal builds from invented sources, committed with the source TIA generates for them, so that every test run checks the reader against TIA Portal, also on machines without it."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T12:00:00Z }
status: stable
---
# Spec 0008: reference projects built by TIA Portal

## Intent

Today the reader is checked against TIA Portal only by hand, on a machine with TIA, through a comparison of a sample's dump with TIA's generated source; the library's tests read only files that our own builder writes, so they can only confirm what we already believe about the format. A reference project closes that gap: invented sources, each set aimed at one part of the format, from which TIA Portal builds a project, its archive and its generated source, all three committed. Every test run then reads the archive TIA wrote and compares each block's dump with what TIA generated for it, on any machine and in CI, also once no TIA Portal is at hand any more. Because the sources are invented, like the demo project's, nothing from a real project enters the repository.

## User scenarios

### The tests check the reader against TIA Portal (P1)

- Given the committed reference projects, when the library's tests run on a machine without TIA Portal, then every block for which a reference project holds generated source is dumped from its archive and compared with that generated source, by the rules of the comparison.
- Given a block whose dump differs from TIA's generated source, when the tests run, then they fail and name the reference project, the block and the first line that differs, with both versions of it.
- Given a block listed as a known difference, with a reason, when its dump still differs, then the tests pass and report it; when it matches, then the tests fail, so that the list is kept true.
- Given a reference project whose archive holds a block for which it holds no generated source, or generated source for which the archive holds no block, when the tests run, then they fail and name the block.

### Build a reference project again (P1)

- Given TIA Portal V21 and a reference project's sources, when the maintainer runs one command, then TIA builds a new project from the sources, and the reference project's archive and generated source are replaced by the ones from this build.
- Given the command runs, when TIA refuses a source, then it names every refused source and its error in one run, and leaves the committed files as they were.
- Given the command runs, then it opens, reads and changes no project but the one it creates, and deletes that project's folder when it is done.

### The reference projects cover the format's risky parts (P2)

- Given the reference projects, then together they hold: every elementary data type with a start value; arrays, structs and data types nested in each other; constants; comments with characters outside ASCII; blocks in nested folders on two PLCs; tag tables with comments; and a know-how protected block.
- Given the know-how protected block, when the tests run, then the reader reports it closed, and no text of its body, which the sources know, appears anywhere in what the reader returns for the archive.
- Given a reference project's tag tables, when the tests run, then the dump of each tag table holds every tag with the name, data type, address and comment that TIA exports for it.

### Only invented content (P1)

- Given an archive anywhere in the repository outside a reference project or with no sources beside it, when the repository check runs, then it fails and names the file.

## Requirements

- FR-001: A reference project MUST consist of its invented sources, the archive TIA Portal built from them, and TIA's generated source of each block of that archive, all committed.
- FR-002: The sources of a reference project MUST NOT hold anything from a real project: no names, source text, values or plant details.
- FR-003: The library's tests MUST compare, for every block of every reference project that has generated source, the reader's dump of the block with TIA's generated source, by the rules `tiadump compare` uses.
- FR-004: A difference MUST fail the tests, naming the reference project, the block and the first differing line, with both versions of it.
- FR-005: A block in the archive without generated source, and generated source without a block in the archive, MUST fail the tests, unless the reference project lists it as known, with a reason.
- FR-006: A reference project MAY list known differences, each with a reason; a listed block whose dump matches MUST fail the tests.
- FR-007: The tests MUST run without TIA Portal, on Windows, macOS and Linux.
- FR-008: One command MUST build a reference project again from its sources with TIA Portal V21 and replace its archive and generated source.
- FR-009: Building MUST report every source TIA refuses, with TIA's error, in one run, and MUST leave the committed files unchanged when any source is refused.
- FR-010: Building MUST NOT open, read or change any project other than the one it creates.
- FR-011: The reference projects MUST together cover every part of the format listed in the third user story.
- FR-012: For each know-how protected block of a reference project, the tests MUST check that the reader reports it closed, and that no line of its body from the sources appears in the dump, the search results or the source the viewer shows.
- FR-013: The tests MUST check that each tag table's dump holds every tag with the name, data type, address and comment of TIA's export of that tag table.
- FR-014: The repository check MUST fail on an archive that is not a reference project's, with sources beside it.
- FR-015: The demo project SHOULD become a reference project, so that the README's pictures can be taken again without TIA Portal.
- FR-016: All reference projects together SHOULD add less than 10 MB to the repository.

## Success criteria

- SC-001: After TIA Portal is no longer available, a change that breaks the reader's output for any covered part of the format fails the tests in CI.
- SC-002: Every part of the format in the third user story is covered by at least one reference project.
- SC-003: Adding a reference project takes writing its sources and running one command.
- SC-004: The reference projects add less than 10 MB to the repository.

## Out of scope

- TIA Portal versions other than V21: TIA builds archives of its own version only, so older versions stay covered by the samples alone.
- Blocks in LAD, FBD, STL or GRAPH: a later spec, on the same mechanism.
- TIA's SimaticML export of compiled blocks, hardware configuration, cross references and OPC UA node sets as expected output.
- Fail-safe blocks, which need STEP 7 Safety and the safety program's password.
- Comments in several project languages.
- The samples: they stay outside the repository, and their comparison stays a manual check.

## Decisions

- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md), new: archives of reference projects, built by TIA Portal from invented sources, are committed with TIA's generated source.
