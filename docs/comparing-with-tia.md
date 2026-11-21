---
type: Guide
title: "Comparing with TIA Portal"
description: "How to compare the reader's output with the source TIA Portal generates, block by block, and how to build the reference projects again, through TIA Openness."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T12:00:00Z }
status: stable
---
# Comparing with TIA Portal

`tools/TiaOpennessExport` drives TIA Portal through Openness: it exports what TIA itself generates, so that `tiadump compare` can check the reader against it, and it builds the reference projects. The result of the last comparison is in section 17 of [the format](FORMAT.md); what is still open is under its Open questions.

## Setup

This needs Windows, TIA Portal V21, and a user in the group `Siemens TIA Openness`, signed in again after joining it. TIA may ask to confirm the first access.

```bash
dotnet build tools/TiaOpennessExport -c Release
```

The project references TIA's Openness assemblies under `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`; set `TiaPublicApi` when TIA is installed elsewhere. It is not in `TiaTools.slnx`, so the solution still builds without TIA.

## Comparing an archive

The exporter retrieves a copy of the archive into an empty folder, writes generated source and SimaticML per block, and closes the copy unsaved. It never compiles or saves, and it skips protected blocks. Keep the folder's path short: TIA refuses a long one and says only "Unable to retrieve the project". Keep both output folders outside this repository, like every archive.

```bash
tools/TiaOpennessExport/bin/Release/net48/TiaOpennessExport.exe archive Project.zap21 C:/Users/me/tc <export folder>
```

```bash
tiadump dump Project.zap21 <dump folder>
```

```bash
tiadump compare <dump folder> <export folder>
```

TIA exports SimaticML only for a compiled block. An archive that was never compiled after an upgrade still gives all its source, and `manifest.tsv` says which exports it refused. To get more XML, compile the retrieved copy:

```bash
tools/TiaOpennessExport/bin/Release/net48/TiaOpennessExport.exe compiled-copy C:/Users/me/tc/Project/Project.ap21 <export folder>
```

This compiles each block that is not fail-safe, exports, and closes unsaved. Only point it at a retrieved copy. Fail-safe blocks need the safety program's password, so to include them, open the copy in TIA yourself, log in to the safety program, compile, and leave it open. Then run `TiaOpennessExport attach <export folder>`, which exports from the open TIA. Close the copy unsaved afterwards.

## Reference projects

A reference project ([spec 0008](../specs/0008-reference-projects.md)) is an invented program that TIA Portal builds, kept in `tests/reference/<name>/` with the archive TIA built and the source it generated, so the library's tests compare the reader with TIA in every run, on any machine ([ADR 0012](adr/0012-reference-projects-are-committed.md)). `FillingLine` is the demo project.

- `sources/`: one folder per PLC, named as the PLC, with `order.txt`, the order in which TIA has to generate the files (what a source names must exist first), and `PLC data types/`, `Program blocks/` and `PLC tags/`. The folders below the first two become the project's folders. A tag table is a `.tsv` of name, data type, address and comment. `protect.txt` names blocks to protect with know-how protection, under a password written in the exporter: it guards nothing invented. Write sources with text outside ASCII as UTF-8 with a byte order mark; TIA reads a source without one in the system's code page. TIA refuses some ordinary words as names, among them `name` in a UDT and `timer` and `NAME` in an FB; the build names each such row.
- Blocks TIA takes no text source for, LAD, FBD and GRAPH, are `.xml` files in SimaticML, listed in `order.txt` like the others and imported into the folder they are in ([spec 0009](../specs/0009-logic-languages.md)). V21's import wants more than its schemas in `PublicAPI/V21/Schemas` say: `<Namespace />` among a block's attributes, and none of `MemoryLayout` on a GRAPH block; in a LAD network the parts of each rung in the order the current flows, a parallel branch closed by an `O` part, an unused output such as a timer's `ET` wired to an `OpenCon`, and in FBD an unwired `en` wired to one; a jump's label wired to its pin `label`; UIds on every `Symbol`, `Component` and `Blank` of an SCL network, and none at all in an STL network. In GRAPH, a network inside `Graph` takes no namespace of its own; an action is its text as tokens, `<Token Text="#valve" />`, with `Event`, `Qualifier` and `Interlock` all written, and every line but the last ends in a line feed inside the token's text; a step must not share a name with a member, since each step becomes one; the interface needs the input `ACK_EF`; and a call in an action is `CALL "Tick"()`, a block without parameters. TIA's import errors name most of this; where one only says `Import failed`, import smaller variants of the block side by side to find the part it refuses.
- `cpu.txt`, in a PLC's folder when needed: the order number of its CPU, which the build takes in the catalog's latest version; without it the PLC gets a CPU 1516-3 PN/DP. `TechnologyObjects` uses a CPU 1517T-3 PN/DP, whose firmware V3.1 takes the newest versions of the motion objects, and a CPU 1511C-1 PN for its counter channels.
- Technology objects ([spec 0013](../specs/0013-technology-objects.md)) are `.to` files in `Technology objects/` and the folders below it, listed in `order.txt` before any block that names them: a line `type` (`TO_PositioningAxis`), a line `version` (`8.0`), then one `parameter<TAB>value` per parameter to set, by the name Openness gives it. A value `@HSC_1` is the CPU's device item of that name, which a counter's `Configuration.Module` takes. Openness refuses some parameters as read-only, such as a PID controller's `Retain.CtrlParams` and `Config.CoolFactor`, and the build names each it refuses. It cannot create measuring inputs, output cams or cam tracks, nor import them. An axis needs no drive when it is virtual, `VirtualAxis.Mode` 1.
- `accept.tsv`, in a PLC's folder when needed: a technology object's name and a compile error TIA gives for it, one per line, which the build accepts. Only for a missing hardware connection, which spec 0013 leaves out: TIA compiles no external encoder without its telegram, and no SSI encoder without hardware. Such an object is archived without a compiled image, so its defaults are not in the archive.
- `<name>.zap21`: the archive TIA built.
- `generated/`: TIA's generated source of each block, its SimaticML export of each block it generates no source for, and its export of each tag table, with `manifest.tsv`, as an export for a comparison holds. To export SimaticML TIA needs the blocks compiled, so the build compiles each PLC, and a compile error counts as a refused source. For each technology object, `TO.<name>.tsv`: every parameter Openness reports, with its .NET type and value; its manifest row has kind `TO`, its own kind as the language and its version as the note.
- `known.tsv`, when needed: `plc`, `kind`, `name`, `reason` for a block whose dump differs from TIA's, or that has no generated source. A listed block that matches again fails the tests, so the list stays true. With kind `TO`, a technology object TIA did not compile, whose parameters without a stored value are then not compared, or `object/parameter` for a value Openness gives in other units than the data block holds.

After changing a reference project's sources, build it again; this needs the setup above, and the work folder's path short:

```bash
tools/TiaOpennessExport/bin/Release/net48/TiaOpennessExport.exe reference C:/Users/me/tr tests/reference/<name>
```

It takes several folders at once, and builds them all in one TIA session. For each it creates a new project from the sources, archives it and exports its generated source, and replaces the committed files only when TIA took every source; otherwise it names each refused source with TIA's error and changes nothing. It opens no other project, and deletes the work folder when it is done. Each build adds a new archive to the history, so build a project again only when its sources changed.

To take the README's pictures again, after a change to how the viewer looks:

```bash
dotnet run --project tools/ReadmePictures -c Release -- tests/reference/FillingLine/FillingLine.zap21 .github/images
```

It renders the viewer without a screen, at the same size every time, so only what changed in the viewer changes in the pictures. Run it on Windows: text is drawn with the machine's fonts, and the current pictures were taken there.
