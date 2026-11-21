---
type: Plan
title: "Plan 0016: GRAPH step times, more STL, ProDiag assignments and classic CPUs"
description: "How spec 0016 is built: the MoreLanguages reference project and its TIA build, then the reader part by part, and where the tests sit."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T16:00:00Z }
status: stable
---
# Plan 0016: GRAPH step times, more STL, ProDiag assignments and classic CPUs

## Order

TIA Portal V21 is a trial on this machine, so everything that needs it came first, in one build; the reader was then written against the committed archive without TIA, as in plan 0009.

1. Sources of `tests/reference/MoreLanguages` for all four parts, and the exporter changes below (FR-011 to FR-013), built in `C:/Users/Simon/tr-lang`. What TIA refused was tried in smaller variants side by side in a throwaway probe project; what it refuses after that is out of the spec with the reason (SC-004). The build showed:
   - V21's catalog still has an S7-300, CPU 315-2 PN/DP V3.2, and compiles its SCL, STL, LAD and FBD;
   - V21 creates a ProDiag function block through Openness and imports assignments to it, but refuses SimaticML with supervisions in every form tried, so supervisions are out;
   - a step's times are the start values of its member's `T_MAX` and `T_WARN`.
2. GRAPH step times (FR-001, FR-002).
3. STL (FR-003, FR-004), with the identifiers it needs: absolute addresses, `P##`, array indexes.
4. ProDiag assignments and the ProDiag OB (FR-005 to FR-008).
5. Classic CPU (FR-009, FR-010): its blocks needed nothing beyond step 3.
6. Search (FR-014), viewer (FR-016), `docs/FORMAT.md`.

## The reference project

`tests/reference/MoreLanguages/sources/`, two PLCs:

- `PLC_More` (S7-1500, the exporter's usual CPU):
  - `Program blocks/Sequence/Timed.xml`: a GRAPH block in SimaticML with a step with both times set, one with the maximum set and one with neither. `Languages/Batch` stays as it is.
  - `Program blocks/Statement/StlIndirect.awl`: an FC with `S7_Optimized_Access := 'FALSE'`, since an S7-1500 allows address registers only in a block with standard access: `L MW[ #place]`, `LAR1`, `L MW[ AR1, P#2.0]`, `+AR1`, `L P##local`, `A[ AR2, P#0.1]`, `OPN DB[ #dbNumber]`, `TAR1`, `TAR2`, `CAR`, `DBNO`, `DBLG`.
  - `Program blocks/Statement/StlForms.awl`: word logic, shifts and rotates, conversions, `JL`, `LOOP`, status bits, `FP`/`FN`, `S`/`R`, `BEC`, constants of every format, and calls of `Common/Blink.scl` with `Common/BlinkDb.db` and as a multi-instance.
  - `Program blocks/Diagnosis/`: `PlantDiag.prodiag`, a ProDiag FB and its instance DB; `Line.scl` with a static member of `PLC data types/Diagnosis/Motor.xml`; `LineDb.xml`, its instance DB assigned to `PlantDiag`; `Plant.xml`, a global DB whose `Motor` member and two array elements are assigned to `PlantDiag`.
  - `PLC tags/IO.tsv`.
- `PLC_Classic` (S7-300): `cpu.txt` names its order number. `Scale.scl` and `ScaleDb.db`, `Classic.awl` with `SD`, `CU`, `R`, `OPN` and absolute addresses, `ClassicLad.xml` and `ClassicFbd.xml` on global tags, `Store.db` with standard access, `Recipe.udt`, a tag table.
- `known.tsv` lists the ProDiag FB and its OB, of which TIA exports nothing.

## Exporter

`tools/TiaOpennessExport/Program.cs`, kept small because the hardware spec changes the same file:

- A PLC folder may hold `cpu.txt`, an order number, looked up in the catalog as before; the default stays the CPU 1516-3. A note in the side sessions' folder told the hardware session.
- `.prodiag` in `order.txt` creates a ProDiag FB and its instance DB through `CreateFB` and `CreateInstanceDB`; the file holds both numbers.
- An `.xml` under `PLC data types` is imported into the type group.
- `Exporter.Block` and `Exporter.Type` export SimaticML beside the generated source when it holds `<Supervisions>` or `<AssignedProDiagFB>`, and drop it otherwise, without reporting a block TIA does not export.
- Software units were tried for supervisions and taken out again: they did not help.

## Reader

All in `src/TiaFormat/Program`, base class library only.

- **Step times.** `TiaProgram.ReadGraph` reads each step member's declared `T_MAX` and `T_WARN` through the interface nodes (`ReadInterfaceNodes`, split from `ReadInterface`); `TiaGraphStep` carries them and `TiaGraph.Write` puts them on a line below the step. `SimaticMlNetworks` reads `MaximumStepTime` and `WarningTime`, so the GRAPH comparison holds both.
- **STL.** `StlRenderer` writes a statement's operand from its tokens after the mnemonic, in order, which covers indirect addresses, status bits, `CALL` with an instance DB, and statements it has no rule for (FR-004). `TiaIdentTable` writes an `Address` as `P#` and the identifier it points to, a `SimpleAccess` without a name as an absolute address, and an array element with its index (`RIDI`).
- **ProDiag.** `TiaProgram.ReadProDiagAssignments` reads the `UDTInstSupervisionContainer` of a global DB and the `BlockInstSupervisionContainer` of an instance DB, naming members and function blocks through the container's own identifiers; `TiaSourceWriter` writes them as comment lines after `END_DATA_BLOCK`, which the source comparison cuts off (FR-007). `TiaProgram` writes a `GlobalCalls` network as `"PlantDiagDb"();   // "PlantDiag"`. Type code `3F` is `Variant`.
- The viewer shows `TiaProject.Document`, written by the same writer, so FR-016 holds by construction.

## Tests

- `TiaReferenceProjectTest.EveryBlockMatchesTiasGeneratedSource` covers the STL blocks and the S7-300's sources, unchanged; `EveryNetworkMatchesTiasExport` the GRAPH block with its step times and the S7-300's LAD and FBD.
- `EveryProDiagAssignmentMatchesTiasExport(project)`: `ReferenceProjects.ProDiagProblems` against `AssignedProDiagFB` in TIA's SimaticML of each data block; MoreLanguages must compare some.
- `ADataBlockListsWhatIsAssignedToAProDiagFunctionBlockAfterItsText_AndTheProDiagObItsCalls` and `SearchFindsAnOperandUsedOnlyInStlWithIndirectAddressing` on the MoreLanguages archive.
- `TiaSourceRenderingTest`: one test per STL and identifier rule. `TiaSourceFileTest`: a network in an unknown form is reported and the block still written (FR-010). `TiaGraphTest`: a step's times in its statement form.

## Decisions

- ADR 0012, ADR 0013. No new ADR: assignments follow ADR 0013's approach, and the classic CPU needed no reader of its own.
