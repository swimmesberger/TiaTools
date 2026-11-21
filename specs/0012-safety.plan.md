---
type: Plan
title: "Plan 0012: safety report"
description: "How spec 0012 is built: where the safety program is stored, the Safety reference project and what the exporter records about it through Openness, the reader and tiadump safety, how its output meets spec 0010's device list, and where the tests sit."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# Plan 0012: safety report

## Order

TIA Portal V21 with STEP 7 Safety is a time-limited trial here, so everything that needs it comes first; the reader is then written against the committed archive without TIA.

1. Write the sources of `tests/reference/Safety`, teach `TiaOpennessExport reference` to build its hardware and safety program and to record TIA's report of it (FR-014, FR-015). Build, commit the archive and TIA's output.
2. Read the stored forms in the new archive with `tiadump inspect`, change one setting at a time in a copy through Openness where a key is stored only once it changes (as section 18 did for OPC UA), and write the result into `docs/FORMAT.md`, a new section 19, marked **[S]** once a test checks it (FR-018).
3. The reader, `TiaSafety`, and `tiadump safety` (FR-001 to FR-013).
4. The reference tests (FR-016, FR-017) and the secret test (FR-003).
5. The viewer's Safety entry and view (FR-020 to FR-025).

## Where it is stored

`docs/FORMAT.md`, section 19, has it, checked on the reference project. The walk over the samples before the build guessed most of it right; it was wrong about the F-CPU's settings, which are dictionary expando keys of the PLC object (`Failsafe_CentralSourceAddress`, `Failsafe_DestinationAddress_LowerBound`, `...UpperBound`, `Failsafe_Central_F_WD_Time`), not of its `CPUFexecUnit` device item, and it could not tell which value is the collective F-signature: `FailsafeCompilationInfo.OverallSignature`, absent in the samples because their program was changed after its last compile. A safety password is found set by `FProgramPasswordPlus` being non-empty; its value is never shown or used.

## The reference project

`tests/reference/Safety/sources/PLC_Safety/`, in the layout of spec 0008 and spec 0009:

- `Program blocks/`: a main safety F-FB in F-FBD for each of the two F-runtime groups, an F-FC in F-LAD that one calls, a fail-safe global DB, and an F-FB with a multi-instance; SimaticML written for this project (programming languages `F_FBD`, `F_LAD`) and imported, as spec 0009 imports LAD and FBD. A standard SCL block that reads the fail-safe DB keeps the two parts apart as TIA requires.
- Hardware: an F-CPU (CPU 1516F-3 PN/DP, the newest firmware the catalog offers) instead of the standard CPU the other reference projects use, an F-DI and an F-DQ in its rack, and an ET 200SP IO device with an F-DI on the CPU's PROFINET subnet. The hardware is described in `sources/hardware.tsv`, in the rows of spec 0010; its builder (`tools/TiaOpennessExport/Hardware.cs` and the hook in `Reference`) is taken unchanged from spec 0010's branch, so whichever lands second keeps the first's copy. The second F-CPU, `PLC_Locked`, is a device there too.
- `safety.tsv`: the settings TIA's defaults would hide, one per line as object, Openness attribute and value: the two F-runtime groups with their names, main safety blocks, maximum cycle times and warning limits; their F-OBs' cycle times; one F-I/O's F-monitoring time set by hand; the F-CPU's default F-monitoring time and its range of F-destination addresses. A line TIA refuses counts as a refused source.
- `PLC_Safety` has no safety password: without one TIA compiles the safety program through Openness.
- `sources/PLC_Locked/`: a second F-CPU with one F-runtime group and one F-FB, no F-I/O. After its program compiled, the exporter sets an invented password, `SafetyBuilder.ReferencePassword`, written in its code like `ReferencePassword`, through `SafetyAdministration.SetSafetyOfflineProgramPassword`, and never logs in with it (FR-019). A `password.txt` beside `order.txt` asks for it, so no other reference project gets one. Setting it counts as a change, so this PLC's collective F-signature is none: the case of a program changed since its last compile, without logging in to compile again.

## Exporter

`tools/TiaOpennessExport`:

- `reference` chooses the CPU from the hardware description instead of always the standard CPU, creates the F-runtime groups through `SafetyAdministration.RuntimeGroups.Create`, applies `safety.tsv` through `SetAttribute`, and compiles; the existing compile already counts errors as refused sources.
- After compiling, for each PLC with a safety program it writes `generated/safety/<PLC>.tsv`, one line per value as object kind, object name, attribute and value, sorted, so a diff shows one value per line: from `SafetyAdministration` its settings, `IsSafetyOfflineProgramPasswordSet`, `ProgramSignatures` and each `RuntimeGroup`'s attributes; from each block with a `SafetySignatureProvider` its `BlockOfflineSignature`; from the F-CPU and each F-I/O every attribute whose name starts with `Failsafe_`, found through `GetAttributeInfos`, so nothing has to be named in advance. Values are written as Openness gives them; the test converts units.
- SimaticML of the fail-safe blocks is exported like spec 0009's, so the existing statement form comparison covers them (FR-017).

## Reader

- `src/TiaFormat/Program/TiaSafety.cs`: `TiaSafety.Read(TiaProgram, string? plc) → TiaSafetyReport`, records per PLC: `TiaSafetyProgram` (settings, password set, signatures, compile time), `TiaRuntimeGroup`, `TiaFailsafeBlock`, `TiaFailsafeIo`, `TiaFailsafeCpu`. Absent keys become TIA's defaults with a flag, as `TiaOpcUa` does. It reads declared attributes and dictionary expando values with what `TiaDatabase` already has; no new decoding is expected.
- `src/TiaFormat/Program/TiaSafetyWriter.cs`: Markdown and JSON, as `TiaOpcUaWriter`.
- `src/TiaDump/TiaDumpCommands.cs`: `safety [--json] [--plc]`.
- Secrets: the reader reads the password attributes only through a check that they are set, and `TiaSecrets` stays the filter for `inspect`.

## Viewer

As plan 0005 did for OPC UA:

- `Program/TiaProject`: every PLC holder of the project itself whose PLC has a `FailsafeInfo` gets a `TiaItem` of kind `Safety` (`TiaItem.SafetyReportKind`), sorted after its OPC UA item and kept in `Reports`, so search, references, the status count and export never see it (FR-020, FR-024). `Safety(item)` reads the PLC's `TiaSafetyPlc` once, when first asked (FR-021); `Document(item)` writes it as `tiadump safety --plc` does, for Copy (FR-023). A block named in the view resolves through `Resolve(item, name)` (FR-022). Finding which PLCs have a safety program is one pass over `FailsafeInfo` objects, not a read of the report, so opening an archive stays as fast.
- `src/TiaViewer/SafetyView`: built in code from Avalonia's controls, like `OpcUaView`. Tabs: *General* (settings, password set or not, signatures, compile time), *F-runtime groups*, *F-blocks* (a virtualised list: kind, number, name, F-signature, generated by TIA; opened by double-click or Enter) and *F-I/O* (the F-CPU's parameters, then one row per F-I/O). A default reads as TIA's value, dimmed and marked default (FR-021).
- `MainWindow`, `KindBadge`, `Glyphs`, `TreeRow.CanExport`: the kind gets the badge `F`, no export, the editor's place, as `OpcUaView` has.

## How it meets spec 0010

Spec 0010 lists devices and their device items with names, slots and I/O addresses; the safety report lists the F-parameters only. An F-I/O is named in the report by the name of its device and the name of its device item, the names spec 0010 lists, and by its first input and output address, so a reader of both can join them on either. Until `TiaHardware` from spec 0010 is on `main`, `TiaSafety` takes the two names by following the device item's `LogicalContainer` to its `DeviceData`, and reads nothing else of the hardware; once it is there, it takes them from `TiaHardware` so both name a device item the same way. Neither report repeats the other.

## Tests

- `tests/TiaFormat.Tests/TiaSafetyReferenceTest.cs`, on `tests/reference/Safety`: `EverySafetyValueMatchesTiasReport` compares the report with `generated/safety/<PLC>.tsv` and names project, PLC, object, value and both versions (FR-016); the rules of that comparison tested on made-up input as `ReferenceProjects` tests its own: `ADifferentValueNamesTheObjectAndBothVersions`, `AValueOnlyTiaReportsFails`, `AValueOnlyTheReportHasFails`.
- One test per acceptance scenario, named after it, on the reference project where it holds the case and on `TiaFileBuilder` files where it does not (no safety program, no PLC, a never-compiled program, an absent key giving the default).
- `TiaSafetyTest`, on `TiaFileBuilder` files: no PLC, a PLC without a safety program, and `APasswordValueIsReportedSetAndNeverShown`, a password value holding a marker that neither form of the report nor `inspect` shows (FR-003). On the reference project, `APasswordIsReportedSetAndNothingOfItAppearsInAnyOutput` looks for the invented password and the stored value in both forms of the report and the dump.
- The statement form of the fail-safe blocks runs through the existing `EveryNetworkMatchesTiasExport` of spec 0009 (FR-017).
- `NoSafetyPasswordAppearsInAnyOutput`: on `PLC_Locked`, the invented password and the stored password values do not occur in the report, the dump, search results or any document the viewer shows (FR-003, FR-025).
- `tests/TiaFormat.Tests/TiaProjectSafetyTest.cs`: the viewer scenarios the library carries, on the reference project opened as a `TiaProject` (entry present and placed, none without a safety program, block rows resolve, Copy text, not searched or exported). The view itself needs a window: checked by rendering it headless as plan 0005 did, pictures on the pull request.

## Docs

- `docs/FORMAT.md`: section 19, Safety; open question 2 (fail-safe FBD compared only by eye) answered by the reference project.
- `GLOSSARY.md`: **Safety program**, **F-runtime group**, **F-signature** (collective and block), **F-I/O**, **F-parameters**, **Safety report**.
- `docs/comparing-with-tia.md`: `safety.tsv` and the safety part of a build.
- README: `tiadump safety` among the commands, and the viewer's Safety entry.
- No new ADR is expected: reading stays read-only and closed parts stay closed under ADR 0011; recording Openness's report beside the generated source follows ADR 0012.
