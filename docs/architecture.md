---
type: Architecture page
title: "Architecture"
description: "How the library, the tool and the viewer are shaped and why."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T12:00:00Z }
status: draft
---
# Architecture

TiaTools reads a TIA Portal project's own database file and turns its PLC program into text, without TIA Portal and without ever writing to the file. One library does the reading; a command line tool and a viewer are thin front ends on it, and a separate Windows tool talks to TIA Portal only to check the library's output.

## Shape

- `src/TiaFormat`: the library. It depends on the base class library only. Its layers build on each other: `Storage` reads the container, frames, values, blobs and relations; `Schema` reads the type descriptions the file carries; `Xml` is a UTF-8 reader for the XML parts; `Program` turns objects into blocks, interfaces, SCL, STL, LAD, FBD and GRAPH text and tag tables, reports the OPC UA configuration (`TiaOpcUa`) and the safety program (`TiaSafety`), writes them as files (`TiaSourceExport`) and holds the viewer's model (`TiaProject`: tree, tokens, references, search).
- `src/TiaDump`: the `tiadump` command line tool, on `TiaFormat` and ConsoleAppFramework.
- `src/TiaViewer`: the viewer, on `TiaFormat` and Avalonia, published through Native AOT.
- `tests/TiaFormat.Tests`: tests of the library, on TUnit. They build small files with `TiaFileBuilder`, and read the reference projects in `tests/reference`, archives TIA Portal built from invented sources ([ADR 0012](adr/0012-reference-projects-are-committed.md)); never a real archive.
- `tests/TiaViewer.Tests`: tests of the viewer's parts that need no window (recent files, file types, version), on TUnit. Menus and dialogs are checked by running the viewer.
- `tools/TiaOpennessExport`: a .NET Framework 4.8 tool on TIA Portal's Openness assemblies, outside the solution. It exports what TIA generates, for `tiadump compare`, and builds the reference projects. Nothing else depends on it.
- `tools/ReadmePictures`: takes the README's pictures by opening the demo project in the viewer's `MainWindow` on Avalonia's headless platform ([ADR 0010](adr/0010-readme-pictures-are-rendered-headless.md)). It is in the solution, so the build catches it when the viewer changes.

## Why it is this shape

- **Read-only, and protected blocks are read as encrypted.** The library opens a file as input and has no code path that writes to it, and a protected block holds only ciphertext, so there is nothing to render but its encrypted form (ADR 0011). Any decryption would need the user's own password, so the library stays safe to point at someone else's project.
- **The library takes no packages.** It can be used from any .NET program, and the viewer can be published through Native AOT, which starts in under a second.
- **Text as TIA generates it.** SCL blocks, data blocks and UDTs are written the way TIA's *Generate source from blocks* writes them, so that a dump can be compared with TIA byte for byte and diffed against other exports. So is STL. LAD, FBD and GRAPH have no such text in TIA, so they are written in a statement form of TiaTools' own, for a coding agent to understand first; the viewer may show them otherwise, but its export writes that text ([ADR 0013](adr/0013-networks-are-shown-as-statements.md)).
- **Colours and references come from stored tokens, not a grammar.** The project stores SCL as tokens; the viewer uses them, and only declarations and the statement form of LAD, FBD and GRAPH go through a small scanner.
- **TIA Portal only for checking.** Openness needs Windows and a TIA licence, so it is kept out of the solution, and the solution builds and tests anywhere.

No TiaTools-specific ADR records these choices yet; the decisions in [the index](adr/index.md) are the ones inherited from the repository template.

## Sources

- [The format](FORMAT.md), section "This repository's reader"
- The project files under `src/`, `tests/` and `tools/TiaOpennessExport`
- [Comparing with TIA Portal](comparing-with-tia.md)
