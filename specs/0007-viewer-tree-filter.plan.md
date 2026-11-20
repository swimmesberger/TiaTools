---
type: Plan
title: "Plan 0007: kind filter and program cycle mark in the viewer's tree"
description: "How spec 0007 is built in src/TiaFormat and src/TiaViewer, and where it is tested."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
---
# Plan 0007: kind filter and program cycle mark in the viewer's tree

## What the format gives

An OB's `ICoreAttributes.Subtype` is `OB.` and its event class (`OB.ProgramCycle`, `OB.Startup`, `OB.CyclicInterrupt`, with `.Safety` after it on a fail-safe one), and its `HandledEvent` relation leads to an object of the matching type (`ProgramCycleEventData`, ...). The two agreed on all 37 OBs of the demo project and two samples, nine event classes, closed OBs included; every program cycle OB there was OB 1. [The format](../docs/FORMAT.md), section 10, gains this, marked **[S]** for that agreement. Whether TIA's own name for the event class (an export's `SecondaryType`) is always the part after `OB.` is **[I]**.

## Modules

- `Program/TiaProgram`, `TiaBlock`: a block's `EventClass` (init property), read from `Subtype` when it starts with `OB.`, up to the next dot; null for other blocks and for an OB without one.
- `Program/TiaProject`, `TiaItem`: `IsProgramCycle`, true for an OB of the project itself whose event class is `ProgramCycle`, or, when it has none, whose number is 1 (FR-008 to FR-010). `Kinds`, the kinds of all items and reports in the tree in the order of FR-001. `Matches(name, kinds)`, the test an item passes to be shown: its name holds the text, whatever its case, and its kind is one of the chosen, or none is chosen (FR-002 to FR-004).
- `src/TiaViewer/MainWindow`: below the name filter, a wrap panel of toggle buttons, one per kind of `Kinds`, each holding a `KindBadge`, built in code when an archive opens (FR-001, FR-006, FR-011). `FillTree` and `Row` take the chosen kinds with the name and use `TiaItem.Matches`; folders expand while either filter is set (FR-005). `Reveal` already leaves a block with no row alone (FR-007).
- `src/TiaViewer/Rows`, `MainWindow.axaml`: `TreeRow.IsProgramCycle`, and in the row template a small start glyph from `Glyphs` after the name with the tip "Program cycle: the program starts here" (FR-008).

## Tests

- `tests/TiaFormat.Tests/TiaProjectTreeFilterTest.cs`, named after the scenarios they carry: the event class read from `Subtype`, without `.Safety`; the mark on a program cycle OB, on several, not on a startup OB numbered 1, on an OB 1 without event class, not on one in a master copy, on a closed one, never on another kind; `Kinds` in order, tag tables and the OPC UA entry after the blocks; `Matches` with no kind, one, several, and with a name. Its own small schema gives a block a number and an environment; the files are built with `TiaFileBuilder`.
- The switches and the mark need a window: they were checked by rendering the viewer headless on the demo project, as `tools/ReadmePictures` does, in light and dark: no switch on, OB alone, DB and tag tables with a name, a block opened that the tree leaves out, and every switch off again. The README's pictures are taken again, as the tree's look changes.
