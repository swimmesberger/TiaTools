---
type: Plan
title: "Plan 0015: watch tables, force tables and cross references"
description: "How spec 0015 is built: what the archive holds for the tables and TIA's cross-reference list, the Tables reference project and the exporter's part, the SQLite reader, and where the tests sit."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# Plan 0015: watch tables, force tables and cross references

## What the archive holds, as found before the spec

Read on the committed reference archives with a scratch probe; it goes into `docs/FORMAT.md` as the reader is built, marked **[S]** only after a check that could have failed.

**Watch and force tables.** Objects of `Siemens.Simatic.Lang.Model.Debugging`: `VarTableData` (a watch table) and `VarTableForceData` (the force table, one per PLC, named `Force table`), each in a `FolderData`, with `ICoreAttributes.Name` and `Comment`, `IVarTableAttributeData` (`MonOptLevel`, `PositionCurrentId`, `VarTableLayout`, the editor's columns as XML) and `CoreObject2IdentContainer` to an identifier table. `AggregatedVarItemContainer` leads to a `VarItemContainerData`, whose `AggregatedVarItems` lead to `VarItemData` objects holding `IVarItemAttributes.ItemData`: XML in the IECPL namespace, `<VarItems><VarLines><VarLine UId="21" /></VarLines></VarItems>` for the empty force table. **[I]** What a filled line holds (operand, format, values, triggers, comment) is seen once the Tables project is built; the identifier table suggests operands are stored as in networks, by UId into it.

**The cross-reference list.** `XRef/XRef.db` beside `System/` in every reference archive, SQLite, with five tables. **[I]** on the Languages archive, the other five archives agree in shape:

| Table | Columns | Holds |
|---|---|---|
| `info` | `version` (10), `savepointID` (a guid) | A version, and perhaps the database state the list belongs to |
| `parts` | `binary` | An object of the database: `OG`, u32 type id, u64 object id, 14 bytes; row 1 is `00`, none |
| `objs` | `scopeObject`, `primaryObject` (both `parts`), `subObject` | An object or a part of it: scope is the PLC, primary the block, tag or type; `subObject` `00` is the object itself, else u32 member ids, a path into its interface (`35 00 00 00 34 00 00 00`: member 0x35, then its member 0x34) |
| `rels` | `leftType`, `left`, `bindingObject`, `leftFilter`, `location`, `rightType`, `right`, `rightFilter`, `relationInfo` | One use: `left` uses `right` (both `objs`); `location` is u32 network or unit number, u32 UId of the part or token, then the right object's kind; `relationInfo` high half is the access, low half 1 for a use and 2 for its inverse |
| `addrs` | `obj`, `filter`, `type`, `range`, `firstBit`, `lastBit` | A tag's absolute address as an area and a bit range |

Accesses seen in `relationInfo`: `0x1` read, `0x2` write, `0x5` label, `0x6` type, `0x8` jump, `0xC` call, `0xF` multi-instance, `0x10` instance data block. The kinds in `leftType` and `rightType`: `0x1010` FC, `0x1020` FB, `0x1060` DB, `0x1065` system instruction, `0x1080` tag, `0x10B0` UDT, `0x1100` label, `0x1120` interface member, `0x1125` UDT member. Several rows occur twice. These names are guesses from the program they describe; the comparison with TIA's list decides them.

What it holds that the references found in the text do not, on Languages: every call in LAD, FBD and STL (the text finds them as uses, or not at all: the TON with its own data block, the multi-instance call of an FB), the tag rather than its table, read and write, a block's own members, calls of system instructions, and the network and part of each use. And on Closed: the protected block's reads and writes of its own members, with their token UIds, in clear.

What the build answered, now in `docs/FORMAT.md` sections 20 and 21: TIA rewrites the list when a block changes, before any compile, and the database names the list's `savepointID` in its `CorePersistenceInfo`, so a list that does not belong to the database is told apart (FR-005, FR-009); neither the list nor TIA's `CrossReferenceService`, asked for a block, a data block or a tag, names a watch or force table, so the reader derives those uses from the entries (FR-007); TIA's own list shows a protected block's uses with the place `?` and none of its own members, as ADR 0015 decided. A tag table offers no cross-reference service, a tag does.

## Order

TIA Portal is a trial on this machine, so everything that needs it comes first; the reader is then written against the committed archive.

1. Sources of `tests/reference/Tables`, the exporter's import and export (FR-014, FR-015). Build into `C:/Users/Simon/tr-tab`, after asking. In the same TIA session, the staleness experiment below. Commit the archive and TIA's output.
2. Read the stored forms in the new archive, write them into `docs/FORMAT.md`: section 1 for the container, a new section for the tables, a new section for the cross-reference list.
3. SQLite reader (ADR 0014), then the cross-reference list, against the committed archives.
4. Watch and force tables: reader, text, dump, show, viewer (FR-001 to FR-004).
5. Cross references: the model, closed blocks, fallback and the text references' fixes, `tiadump xref`, the viewer (FR-005 to FR-012).
6. The reference tests (FR-016, FR-017), the README, the `read-tia-project` skill.

## The reference project

`tests/reference/Tables/sources/PLC_Tables/`, in the layout of spec 0008 and 0009:

- The program: a copy of Languages' sources, which already cover LAD, FBD, STL, GRAPH, SCL, timers, a counter, calls and a multi-instance; Languages itself stays as it is. Added to it: a global DB `Recipe` with a struct and an array, of a UDT; an SCL FC writing and reading its members; a tag read through its absolute address in STL; `Vault/Sealed.scl`, listed in `protect.txt`, which calls `Limit` and writes a tag; and `Main`, the program cycle OB, calling the FBs through instance DBs.
- `Watch and force tables/`: one SimaticML file per watch table, as TIA exports one (`SW.WatchAndForceTables.PlcWatchTable`, no schema is published, so the first is written from an export of an empty table TIA makes), and `Force table.xml`. Subfolders become table groups. `Commissioning/Tank.xml` covers the entries the seventh user story lists.
- Openness can create a watch table but not its entries, and has no create for a force table, so both are imported: `PlcWatchTableComposition.Import`, `PlcForceTableComposition.Import`. The import refuses `ModifyIntention` and `ForceIntention` as read-only, and the export leaves them and the address beside a name out, so the selection box is read but checked against no export.

## Exporter

`tools/TiaOpennessExport/Program.cs`:

- `BuildPlc` imports the tables after the blocks, into their groups (`PlcWatchAndForceTableUserGroupComposition`), and counts a refused import as a refused source (spec 0009, FR-009).
- `Exporter.ExportPlc` exports each watch table and the force table as SimaticML, `WATCH.<name>.xml` and `FORCE.<name>.xml`, listed in `manifest.tsv`.
- `Exporter.CrossReferences` writes `xref.tsv` per PLC: for each block, data block, tag table, watch table and the force table, `GetService<CrossReferenceService>().GetCrossReferences(CrossReferenceFilter.AllObjects)`, walking `Sources`, their `Children` and `References` down to each `Location`, one row per location: source path and name, its type and address, the reference's name, type and address, `ReferenceType`, `Access`, `ReferenceLocation`, `ReferencedAsName`. Only for a project the exporter built, as the other reference outputs.
- The staleness experiment, in the `reference` run for Tables only, after the archive is written: change one SCL block's body through its source without compiling, archive to the work folder, and change it back. The second archive stays in the work folder, outside the repository; what its list holds goes into `docs/FORMAT.md`. A rebuilt exporter makes TIA ask for Openness access again; the user answers "Yes to all".

## Reader

All in `src/TiaFormat`, base class library only.

- `Storage/SqliteFile`: a read-only reader of the SQLite file format, enough for this list: the 100 byte header (page size, reserved bytes, UTF-8 only), `sqlite_schema` to find a table's root page by name, table b-tree interior and leaf pages, cell payloads with overflow pages, and records with every serial type. No indexes, no WAL, no writing; a file it does not understand is reported as unreadable and the reader falls back (FR-009). It reads from a byte array, so the archive's entry is read into memory like `PEData.plf`.
- `Storage/TiaDatabase`, or a small sibling: open `XRef/XRef.db` from the same archive, folder or project the database came from; absent is not an error.
- `Program/TiaCrossReferences`: reads `parts`, `objs`, `rels` into uses of object by object with access and place; resolves objects to blocks, tags (`EAMTZTagData`, their table), members (the member path through the block's interface, by member id) and tables; drops the inverse rows and duplicates; drops, for a closed block, every row whose user is a member of its own or that carries a place inside it, and the place of the others (FR-008). Whether the list describes the current program is decided by what the experiment finds.
- The place: the network number, and the line of the user's text: the first line of that network that names what is used, else the network's first line. A call through an instance names the instance, not the block it calls, so `TiaCrossReference.Instance` carries the instance, matched by place in SCL and by type in LAD and FBD, and the line is the one that calls it; a use in the interface goes to the line that declares its member (FORMAT.md section 21). Mapping each part's or token's UId to its line, and GRAPH's units to steps and transitions, is left for later; the UId is kept in `TiaCrossReference`.
- `Program/TiaWatchTable`: watch and force tables read from `VarTableData` and `VarTableForceData`, their lines resolved through the identifier table; `TiaSourceWriter.WriteWatchTable` writes them as text in the layout of a tag table: one entry per line, `"Tag" : Format`, then `:= value` and the triggers when set, and the comment after `//`; a comment line as a comment.
- `TiaSourceExport` writes `.watch` and `.force` files, listed in `index.tsv` with kind `Watch` and `Force`.
- `TiaProject`: items of kind `Watch` and `Force` in the tree under the PLC, after the tag tables; references come from `TiaCrossReferences` where it holds, and `TiaReference` gains the access and the tag or member used. The text-based references stay as the fallback, with `TiaProject.ReferencesFromText` saying which is in use (FR-009). `TiaDocument`'s scanner marks a call in statement form, and takes a quoted name at the start of a statement-form line as global (FR-010).
- `src/TiaDump`: `tiadump xref <archive> [name] [--plc]`, one use per line: used object, user, access, place.
- `src/TiaViewer`: the Used by and Uses lists show the access and the tag or member; for a tag table they list each tag's uses with the tag's name; the note about closed blocks changes with the source of the references.

## Tests

- `SqliteFileTest`: the `XRef.db` of every reference archive: tables found, row counts as `sqlite3` gives them, written into the test; records of each serial type that occurs; a file with a bad header is refused. Overflow pages are not in any reference archive's list; if none occurs after the build either, that part is **[U]** in the format page.
- `TiaReferenceProjectTest.EveryWatchAndForceTableEntryMatchesTiasExport(project)`: each entry from the archive against the SimaticML TIA exported, field by field (FR-016).
- `TiaReferenceProjectTest.EveryCrossReferenceMatchesTias(project)`: for each project with `xref.tsv`, the set of (object used, object using it, access, network) from the archive against TIA's list read by the same rules, both ways; differences named; `known.tsv` rows of kind `xref` excused, a stale one fails (FR-016, FR-017).
- One test per acceptance scenario of the first five stories, named after it, on the Tables archive: a watch table's entries, the comment line, the empty force table writes no file, a call in each language, a watch table among a tag's uses, a closed block's uses without places or own members, an open block's call of a closed one.
- The fallback (FR-009): an archive without `XRef.db`, built with `TiaFileBuilder`, gives the text's references and says so. FR-010 on the Languages archive, whose `XRef.db` is ignored for that test.
- `tests/TiaViewer.Tests`: the tree has the tables under the PLC.

## Decisions

- ADR 0011, ADR 0012.
- ADR 0014, new: the SQLite reader of our own.
- ADR 0015, new: what a closed block uses is shown. With it, `AGENTS.md`'s rule on protected blocks, the glossary's **Closed** and `docs/FORMAT.md` section 15 say "nothing of its body" instead of "nothing more", in the same change.
