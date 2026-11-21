---
type: Plan
title: "Plan 0008: reference projects built by TIA Portal"
description: "How spec 0008 is built: where reference projects live, how TiaOpennessExport builds them, and how the library's tests and the repository check use them."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T12:00:00Z }
status: stable
---
# Plan 0008: reference projects built by TIA Portal

## Layout

One folder per reference project under `tests/reference/<name>/`:

- `sources/`: one folder per PLC, named as the PLC, each holding `order.txt`, `PLC data types/`, `Program blocks/` and `PLC tags/` as the demo's sources did. Optional `protect.txt` lists blocks to know-how protect, one per line. Sources with text outside ASCII carry a UTF-8 byte order mark, or TIA reads them in the system's code page.
- `<name>.zap21`: the archive, written with `ProjectArchivationMode.DiscardRestorableDataAndCompressed` to keep it small (the demo's is 0.5 MB).
- `generated/`: what `TiaOpennessExport` already writes for a comparison (generated source per block, tag tables as SimaticML, `manifest.tsv`), so `tiadump compare` reads it unchanged. Block XML is left out: TIA refuses it for uncompiled blocks, and spec 0008 does not ask for it.
- `known.tsv`, optional: `plc`, `kind`, `name`, `reason` for blocks known to differ or known to have no generated source (FR-005, FR-006).

The first set, each small and aimed at one part (FR-011):

| Name | Covers |
|---|---|
| `Types` | every elementary type as a member with a start value, in a global DB, an FB's interface and a UDT; constants; `Array` of struct, of UDT, multi-dimensional; nested UDTs; `String[n]`, `WString`, `Char` |
| `Text` | comments and string start values with umlauts, accents, CJK and emoji, quotes and `//` inside strings, block titles and comments |
| `Plcs` | two PLCs with the same block names, blocks four folders deep, tag tables with comments on both |
| `Closed` | one FB know-how protected with an invented password, called from an open FB; its body holds marker lines the test looks for |

`demo/` moves to `tests/reference/FillingLine/sources/PLC_Line/` (FR-015), named after the project and PLC it always had, so the README's pictures do not change; it gains its archive and generated source, and `tools/ReadmePictures` reads the committed archive.

## Building (FR-008 to FR-010)

`tools/TiaOpennessExport` gains `reference <empty work dir> <folder>...`, which replaces the `demo` command and builds every folder given in one TIA session, since starting TIA takes the longest:

1. Create a new project in the work dir, add one CPU per PLC folder, then tag tables and blocks per `order.txt`, collecting every refused source (FR-009).
2. Protect the blocks of `protect.txt` through `PlcBlockProtectionProvider.Protect(SecureString)`. The password is invented and written in the command's code: it guards nothing, and the reader never uses it.
3. Archive to a temporary file, then export generated source and tag tables from the same open project with the existing `Export`.
4. Only when all of that succeeded, replace `<name>.zap21` and `generated/`; close the project and delete the work dir.

Block XML is left out of the export, since TIA refuses it for uncompiled blocks; tag tables keep theirs.

## Tests (FR-003 to FR-007, FR-012, FR-013)

- The folder walk and matching of `src/TiaDump/ExportComparison.cs` move into `TiaFolderComparison` in `src/TiaFormat/Program/TiaSourceComparison.cs`, next to the text rules already there, returning a `TiaComparedBlock` per block instead of printing; `tiadump compare` prints them as before.
- `tests/TiaFormat.Tests/TiaReferenceProjectTest.cs`, one data-driven test per reference project, finding `tests/reference` from the test assembly's folder upwards: dump the archive with `TiaSourceExport` into a temporary folder, compare with `generated/`, apply `known.tsv`, and fail with project, block and first differing line. `ReferenceProjects` in the test project finds, dumps and judges them; its rules are tested on made-up comparison results, one test per scenario: `ADifferenceNamesTheProjectTheBlockAndTheFirstDifferingLineInBothVersions`, `AKnownDifferenceThatStillDiffersPasses`, `AKnownDifferenceThatMatchesFails`, `ABlockWithoutGeneratedSourceFails`, `GeneratedSourceWithoutABlockInTheArchiveFails`. On the real ones: `EveryBlockMatchesTiasGeneratedSource`, `AClosedBlockShowsNothingOfItsBody`, `EveryTagMatchesTiasExport`.
- The closed block's test reads its body lines from `sources/` and searches the dump, `TiaProject.Search` and `TiaProject.Document` for each.
- The tag table test reads TIA's SimaticML of each tag table from `generated/` with `System.Xml.Linq` and compares, tag by tag in table order, name, data type, logical address, comment and external access. Constants are left out: TIA's export of a tag table leaves out the system constants the reader lists, and the sources define no constants of their own.
- CI runs `make check` on Linux; a second job of `check.yml` runs the library's tests on Windows and macOS (FR-007). `.gitattributes` marks the archives binary.

## Repository check (FR-014)

`tools/check_repo.py` gains a step: every `*.zap*`, `*.ap*`, `*.al*` and `PEData.plf` must sit at `tests/reference/<name>/<name>.zap21` with a `sources/` folder beside it. Tested in `tools/tests` like its other steps.

## Docs

- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md), new.
- `GLOSSARY.md`: **Reference project**; **Demo project** becomes one of them.
- `AGENTS.md`, rules: reference projects join the demo project as the only projects whose content may appear here.
- `docs/comparing-with-tia.md`: building a reference project again. `docs/FORMAT.md`: claims the reference projects confirm become **[S]**, and the parts they cover are named in section 17.
