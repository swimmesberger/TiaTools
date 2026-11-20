---
type: Plan
title: "Plan 0005: OPC UA report in the viewer"
description: "How spec 0005 is built in src/TiaFormat and src/TiaViewer, and where it is tested."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
---
# Plan 0005: OPC UA report in the viewer

## Modules

- `Program/TiaProject`: while the tree is built, every PLC holder of the project itself gets a `TiaItem` of kind `OPC UA` (`TiaItem.OpcUaReportKind`, `IsReport`, `ReportPlc`). It sorts before the PLC's folders. These items are kept in `Reports`, not in `Items`, so search, references, the status count and export never see them (FR-005). `Document` makes a report's text the first time it is asked for, with `TiaOpcUa.Read(program, plc)` and `TiaOpcUaWriter.WriteMarkdown` under the title `tiadump` uses (`TiaOpcUaWriter.Title`), as a document without tokens (FR-002, FR-003). `ClosedBlocksAround` is 0 for a report.
- `Program/TiaOpcUa`: `Read` takes an optional PLC name, which `tiadump opcua --plc` now uses too.
- `src/TiaViewer`: `KindBadge` and `Glyphs` give the kind the badge `OPC` and a colour of its own (FR-007). `TreeRow.CanExport` hides the export of a report in the context menu, and its copy item reads "Copy report". The heading hides *Export…* for a report, and *File › Export Block…* is disabled (FR-006). The PLC's count leaves the report out.

## Tests

`tests/TiaFormat.Tests/TiaProjectOpcUaTest.cs`, one per acceptance scenario, on the invented project of `TiaOpcUaFixture` (shared with `TiaOpcUaTest`) opened as a `TiaProject`. The viewer's own changes (badge, hidden export, heading) need a window. They were checked by rendering the viewer headless on that invented project, in light and dark, with a scratch program like `tools/ReadmePictures`. FR-003 holds by construction: the report is made in `Document`, which opening never calls for it.
