---
type: Plan
title: "Plan 0005: OPC UA view in the viewer"
description: "How spec 0005 is built in src/TiaFormat and src/TiaViewer, and where it is tested."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
---
# Plan 0005: OPC UA view in the viewer

## Modules

- `Program/TiaProject`: while the tree is built, every PLC holder of the project itself gets a `TiaItem` of kind `OPC UA` (`TiaItem.OpcUaReportKind`, `IsReport`, `ReportPlc`), sorted before the PLC's folders. These items are kept in `Reports`, not in `Items`, so search, references, the status count and export never see them (FR-011). `OpcUa(item)` reads the PLC's `TiaOpcUaPlc` once, when first asked (FR-002); `Document(item)` writes it as `tiadump opcua --plc` does, for Copy (FR-010). `ClosedBlocksAround` is 0 for it. A data block or tag of the view resolves through `Resolve(item, name)`, as a name in the source does (FR-007).
- `Program/TiaOpcUa`: `Read` takes an optional PLC name, which `tiadump opcua --plc` uses too. `TiaOpcUaSecurityPolicy.Name` gives TIA's name of a policy key, inferred from the research note (FR-005).
- `src/TiaViewer/OpcUaView`: the view, built in code from Avalonia's controls without bindings (FR-013). Tabs: *Server* with the sections General, Options, Security › Secure channel and Security › User authentication (FR-003, FR-004); *Server interfaces and roles* (FR-009); *Data blocks* and *Tags* as virtualised lists with a header row, opened by double-click or Enter (FR-006, FR-007). Settings are read-only check boxes; one not stored is dimmed and indeterminate. Inferred readings say so beside them or in a footnote (FR-008).
- `MainWindow`: a report shows `OpcUaView` in the editor's place and hides the Used by and Uses lists, giving their room to the view and back to the next block. `KindBadge` and `Glyphs` give the kind the badge `OPC`; `TreeRow.CanExport` and the heading hide any export of it (FR-011).

## Tests

`tests/TiaFormat.Tests/TiaProjectOpcUaTest.cs`, one per acceptance scenario that the library carries, on the invented project of `TiaOpcUaFixture` (shared with `TiaOpcUaTest`) opened as a `TiaProject`. The view itself needs a window: it was checked by rendering the viewer headless on that invented project, every tab in light and the server page in dark, with a scratch program like `tools/ReadmePictures`; the pictures are on the pull request. FR-012 rests on spec 0004's reader, which the view only shows.
