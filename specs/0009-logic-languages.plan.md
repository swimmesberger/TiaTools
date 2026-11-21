---
type: Plan
title: "Plan 0009: LAD, FBD, STL and GRAPH blocks"
description: "How spec 0009 is built: the Languages reference project, the exporter's import and compile, the statement form and the GRAPH reader, and where the tests sit."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T14:00:00Z }
status: stable
---
# Plan 0009: LAD, FBD, STL and GRAPH blocks

## Order

TIA Portal is at hand only for a while, so everything that needs it came first; the reader was then written against the committed archive without TIA.

1. Write the sources of `tests/reference/Languages` and teach `TiaOpennessExport reference` to import, compile and export them (FR-007 to FR-009). Build, commit the archive and TIA's output.
2. Read the stored forms in the new archive with `tiadump inspect` and the raw network files of a dump, and write each into `docs/FORMAT.md`, section 13, marked **[S]** once a test checks it.
3. STL renderer, compared with generated source through the existing comparison (FR-004).
4. Statement form for LAD and FBD (FR-001 to FR-003, FR-006), then GRAPH (FR-005).
5. The reference tests of the statement form (FR-010), search (FR-011).

## The reference project

`tests/reference/Languages/sources/PLC_Languages/`, in the layout of spec 0008:

- `Program blocks/` holds the text sources (`Common/Limit.scl`, `Pulse.scl` and `Tick.scl` that the others call, `LampDelay.db`, the `IEC_TIMER` a TON uses, and `Statement/StlBasics.awl`) and the SimaticML of the blocks TIA takes no text source for, written for this project from TIA's schemas in `PublicAPI/V21/Schemas`: `Ladder/LadLogic.xml`, `Ladder/LadBoxes.xml`, `Ladder/Mixed.xml`, `Function/FbdLogic.xml`, `Function/FbdBoxes.xml` and `Sequence/Batch.xml`. `order.txt` lists both kinds in one order.
- One block per concern rather than one per language, so a failure points at one thing: logic (contacts, coils, edges, jump) apart from boxes (move, compare, add, timers, counter, calls), in LAD and in FBD alike; `Mixed` is LAD with an SCL and an STL network; `Batch` is the GRAPH sequence.
- The block numbers are fixed in the files, so a build does not renumber them.
- Fail-safe languages stay out: they need STEP 7 Safety's password.
- What V21's import wants beyond its schemas is in `docs/comparing-with-tia.md`.

## Exporter

`tools/TiaOpennessExport/Program.cs`:

- `BuildPlc` imports each `.xml` with `PlcBlockComposition.Import(file, ImportOptions.None)` into the folder its path names, and reports an import error as it reports a refused source.
- After all sources, it compiles the PLC software through `ICompilable` and prints every error with its path; any error is a refusal (FR-009).
- `Exporter.Block` keeps block XML off for blocks TIA generates source for, as before, and exports SimaticML for the others into `generated/`, listed in `manifest.tsv`'s `xml` column, as for tag tables.
- Openness creates no LAD, FBD, STL or GRAPH block of its own (`CreateFB` takes ProDiag only), so there is no way to let TIA write a first version of such a file.

## Reader

All in `src/TiaFormat/Program`, base class library only.

- `TiaProgram.Networks` gives each network its comment, from the relation `CompileUnitComment`.
- `TiaIdentTable` keeps a called block's parameters with their sections (`Parameters`), and a literal as its source writes it (`SV`).
- `StlRenderer` writes the stored `Statements` as TIA's generated source does; `TiaSourceWriter` writes an STL block's networks under `NETWORK` and `TITLE`, and names the file `.awl`.
- `TiaFlgNet` is a network as a graph of parts, operands, wires and labels, read from the stored form; `NetworkStatements` writes it in statement form, and gives a GRAPH network's condition. `FlgNetRenderer` is now these two in a row.
- `TiaGraph` is a GRAPH block: its permanent instructions, steps with actions, supervision and interlock, transitions, branches and connections, read from all its units together (`TiaProgram.ReadGraph`), and written in statement form with each transition's steps resolved through the branches.
- `TiaSourceWriter` writes a data block of a system type with TIA's `InstructionName` and `LibVersion` (`TiaProgram.InstructionVersion`).
- `TiaSourceExport` names files `.lad`, `.fbd`, `.graph` and `.awl`; the viewer shows `TiaProject.Document`, which uses the same writer, so FR-001 holds for every output by construction.

## Tests

- `TiaReferenceProjectTest.EveryBlockMatchesTiasGeneratedSource` covers STL and the system type's data block; a block TIA exported as SimaticML is compared by the next test instead (`ReferenceProjects.ExportedAsXml`).
- `EveryNetworkMatchesTiasExport(project)`: for each block `manifest.tsv` lists with SimaticML, each LAD and FBD network's title, comment and statement form from the archive equal those TIA's export gives, read by `SimaticMlNetworks`; a GRAPH block's statement form likewise, line by line. A project with such blocks must have compared at least one.
- `ANetworkOfAnotherLanguageInALadderBlockIsShownInItsOwnLanguage` (FR-006) and `SearchFindsAnOperandUsedOnlyInLadderAndFunctionBlockNetworks` (FR-011) on the Languages archive.
- `NetworkStatementsTest`: one test per rule of the statement form, on networks built in the test. `TiaGraphTest`: branches resolved, a jump, an empty condition, a step's actions. `TiaSourceRenderingTest`: a fail-safe network read from its stored form (FR-012), an STL call's layout.

## Decisions

- ADR 0012 (reference projects), ADR 0013 (statement form, new).
