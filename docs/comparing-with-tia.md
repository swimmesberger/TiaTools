---
type: Guide
title: "Comparing with TIA Portal"
description: "How to compare the reader's output with the source TIA Portal generates, block by block, and how to build the demo project, through TIA Openness."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T12:00:00Z }
status: stable
---
# Comparing with TIA Portal

`tools/TiaOpennessExport` drives TIA Portal through Openness: it exports what TIA itself generates, so that `tiadump compare` can check the reader against it, and it builds the demo project. The result of the last comparison is in section 17 of [the format](FORMAT.md); what is still open is under its Open questions.

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

## The demo project

The demo project's program is in `demo/` as SCL sources, with tag tables and `order.txt`, the order to generate them in. This makes a new project from them and archives it, for pictures and for trying the tools without a project of your own:

```bash
tools/TiaOpennessExport/bin/Release/net48/TiaOpennessExport.exe demo demo C:/Users/me/td <out folder>
```

The archive stays outside this repository, as every archive does. To take the README's pictures from it again, after a change to how the viewer looks:

```bash
dotnet run --project tools/ReadmePictures -c Release -- <out folder>/FillingLine.zap21 .github/images
```

It renders the viewer without a screen, at the same size every time, so only what changed in the viewer changes in the pictures. Run it on Windows: text is drawn with the machine's fonts, and the current pictures were taken there.
