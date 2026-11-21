---
type: Plan
title: "Plan 0013: Technology objects"
description: "How spec 0013 is built: the TechnologyObjects reference project, the exporter's creating and parameter export, where TIA stores a technology object, the reader and the tests."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T16:00:00Z }
status: stable
---
# Plan 0013: Technology objects

## What a probe showed

A throwaway project (never committed) with ten technology objects on a 1516-3 PN/DP and a 1511C-1 PN, built through Openness V21:

- **Today** the reader lists none of them: they are objects of `Siemens.Simatic.Lang.Model.Blocks.TechnologicalDataBlockData`, not `DataBlockData`, so `ReadBlocks` skips them. The dump, `list`, `show` and the viewer show nothing.
- **Kind and version**: `ICoreAttributes.Subtype`, `DB.TO.SMC.TO_SpeedAxis.V6.0` for an axis, `DB.TO.PID.Compact.Compact_2.0` for PID_Compact, `DB.TO.TM.HSC1500` for a high-speed counter. `IGeneralDataBlockSourceData.OfName` names the type (`TO_SpeedAxis`, `PID_Compact_1500`), `IPlcHeaderData.HeaderVersion` or the type's `IInterfaceLibraryData.Version` the version. `Number`, the folder (`AggregatingFolder`) and the PLC as for any block.
- **Parameters**: the object's `CurrentInterface` is a root of kind `Values` whose `UsedParts[0]` is the type's root, `IsFBT`, with the type's structures (`TO_Struct_DynamicLimits`, ...) as further parts, all in clear. The root's `<V p="58:51" v="1234.0"/>` are the values stored on the object; the reader's `ReadSetValues` already names them (`DynamicLimits.MaxVelocity`). Defaults are not declared anywhere in the type; they are in the compiled image (`LoadableBinaries`, `VolatileBuffer`), 3000.0 for `Actor.DriveParameter.ReferenceSpeed` at its offset. (The probe thought TIA writes an image even for an object whose compile failed; the reference build showed it does not: such an object's buffers are empty.)
- **Names**: a positioning axis has the speed axis as member `Base`; TIA's names leave `Base.` out (`DynamicDefaults.Velocity`, stored as `Base.DynamicDefaults.Velocity`).
- **Protection**: PID_Compact, PID_3Step, PID_Temp, High_Speed_Counter and SSI_Absolute_Encoder have `IsKnowHowProtected` and `Protection = SystemKnowHowProtection`, with values and interface in clear. The axes have `NoProtection`.
- **Openness**: `TechnologicalObjects.Create(name, type, version)` creates TO_SpeedAxis, TO_PositioningAxis, TO_SynchronousAxis and TO_ExternalEncoder at 6.0 (the newest a 1516-3 at V2.9 takes), PID_Compact 2.4, PID_3Step 2.3, PID_Temp 1.1, High_Speed_Counter 4.0 and SSI_Absolute_Encoder 3.1. It refuses TO_MeasuringInput, TO_OutputCam and TO_CamTrack ("cannot be created by Openness"), and so does `Import` of SimaticML for them ("IMExport of this TO is not supported"); TO_Counter does not exist. A CPU 1517T-3 PN/DP at V3.1, the newest firmware in V21's catalog, takes the axes at 8.0 and PID_Compact at 3.0. A counter's `Configuration.Module` takes a device item, such as the 1511C's `HSC_1`, not text. `Parameters` lists 36 to 161 parameters per object, name and value; some are read-only through Openness (`Retain.CtrlParams.*`), and those starting with `_` are settings outside the data block. `Export` refuses an object that is not compiled, and a high-speed counter always.
- **Compile**: virtual axes and PID compile; the synchronous axis, external encoder, counter and SSI encoder fail only for missing telegram addresses or hardware.

## Order

TIA is at hand only for a while, so everything that needs it comes first.

1. Exporter and sources (FR-006 to FR-008): build `tests/reference/TechnologyObjects`, commit the archive and TIA's parameter values.
2. Storage, written into `docs/FORMAT.md`, new section 19, each claim marked **[S]** once a test checks it.
3. Reader: technology objects and their parameters (FR-001 to FR-005), tests against the reference project (FR-009).
4. Viewer and search (FR-010, FR-011).

## The reference project

`tests/reference/TechnologyObjects/sources/`, in the layout of spec 0008, two PLCs:

- `PLC_Motion`, a CPU 1517T-3 PN/DP at V3.1, for the newest technology versions the catalog has. In `Technology objects/Axes/`: `Spindle` (speed axis), `Feed` (positioning axis), `Follower` (synchronous axis), `Wheel` (external encoder); in `Technology objects/Control/`: `Oven` (PID_Temp), `Level` (PID_Compact), `Valve` (PID_3Step). Axes are virtual (`VirtualAxis.Mode` 1), so they need no drive.
- `PLC_Count`, a 1511C-1 PN, whose onboard counter channel `HSC_1` `Count` (High_Speed_Counter) is assigned to. `Angle` (SSI_Absolute_Encoder) gets none: TIA refuses an onboard channel for it, and also refuses to set its counting limit.
- `cpu.txt` names a PLC's CPU by order number, in the form the languages session's spec 0016 brings, so the branches agree; the catalog's latest version is taken. Without it a PLC gets the 1516-3 PN/DP the other reference projects use.
- A technology object is a file `<name>.to` in its folder: `type`, `version` and one `parameter<TAB>value` line per parameter set away from its default, listed in `order.txt` like the other sources. A value `@HSC_1` is the CPU's device item of that name, for a counter's `Configuration.Module`.
- Every object sets at least one parameter of its own data block away from the default, of several data types (LReal, Real, Bool, DInt, UDInt), in nested structures, and one inherited through `Base`.
- `accept.tsv` names the compile errors the build accepts: an object, and TIA's message that it has no hardware assigned (`Angle`), or no telegram (`Wheel`, an external encoder, which has no virtual mode). Any other compile error refuses the build (FR-008).
- `known.tsv` lists, with kind `TO`: `Wheel` and `Angle`, which TIA did not compile, so their defaults are not in the archive; and four counter parameters that Openness gives in other units than the data block holds.

Measuring inputs, output cams and cam tracks are left out: Openness V21 neither creates nor imports them, and the user chose to read them unchecked rather than add them by hand (spec, Out of scope).

## Exporter

`tools/TiaOpennessExport/Program.cs`:

- `Reference` reads the CPU from `cpu.txt`; `BuildPlc` creates each `.to` with `TechnologicalObjectGroup` and its user groups and sets each parameter with the value converted to the type Openness reports, trying all and naming each that TIA refuses. A refused object or parameter is a refused source.
- After the compile, a compile error is accepted only if `accept.tsv` lists it by object and message; the rest of TIA's error count refuses the build.
- `Exporter` writes, per technology object, `generated/<PLC>/TO.<name>.tsv`: every parameter's name, .NET type and value as Openness gives them, invariant culture; and a manifest row `TO`, with `OfSystemLibElement` in the language column, the file in the xml column and `OfSystemLibVersion` in the note.

## Reader

All in `src/TiaFormat/Program`, base class library only.

- `TiaTechnologyObject`: name, kind (`TO_PositioningAxis`), version, number, folder, PLC, protection, and its frame. `TiaProgram.TechnologyObjects` reads them from `TechnologicalDataBlockData`, apart from `Blocks`, so nothing that lists blocks changes.
- `TiaProgram.ReadParameters(technologyObject)`: every leaf member of the type's interface, followed into the type's structures and arrays, with its value: the object's stored value, else the compiled image, the retain or the volatile buffer by the member's section, at the sum of the offsets along its path, an array element by its type's `optSize`. A path drops `Base.` as TIA does. Section 19 has the layout.
- Which members are parameters (FR-003): no stored mark gives TIA's list (the set point bit covers most of an axis's, none of a counter's), so every member is shown, and the test requires every one TIA reports.
- `TiaSourceWriter` writes a technology object as text of our own, in the dump as `<name>.to`:

  ```
  TECHNOLOGY_OBJECT "Feed" : TO_PositioningAxis
  { VERSION := '8.0'; NUMBER := 2 }
     Mechanics.LeadScrew : LReal := 12.5;
     PositionLimits_SW.Active : Bool := true;
  END_TECHNOLOGY_OBJECT
  ```

  values written as a data block's source writes start values. `index.tsv` gets a row of kind `TO`.
- `TiaProject` adds the technology objects to its items, in the tree under the PLC in `Technology objects` and their folders, so the viewer shows and searches them and `Resolve` finds them by name (FR-010, FR-011).

## Tests

- `TiaReferenceProjectTest.EveryTechnologyObjectParameterMatchesTia(project)` (FR-002, FR-003, FR-009): each object's kind and version, and each parameter not starting with `_` and not a device item must be among the reader's and equal its value, as numbers where both are numbers; `known.tsv` as above, and an entry that no longer differs fails.
- `TiaTechnologyObjectTest`, on the reference project: `ASystemProtectedTechnologyObjectShowsItsParameters` (FR-005), `ATechnologyObjectIsDumpedToAFileOfItsOwnAndListedInTheIndex` (FR-001), `TheTreeShowsATechnologyObjectInItsFolderUnderTechnologyObjects`, `SearchFindsATechnologyObject` (FR-010), `ANameInCodeResolvesToTheTechnologyObject` (FR-011); on files built with `TiaFileBuilder`: `ATechnologyObjectIsListedApartFromTheBlocks`, `ATechnologyObjectOfAnUncheckedKindIsShownByTheSameRules` (FR-004), `AnEncryptedPartOfATechnologyObjectStaysClosed` (FR-005).
- FR-012 rests on the reader's rule, and on the two uncompiled objects of the reference project, whose dump shows their parameters without a value.

## Decisions

- ADR 0012 (reference projects), ADR 0011 (protection), ADR 0014 (system-protected technology objects, new). The ADR number is the next free one when this merges; renumber before the pull request if `main` moved.
