---
type: Specification
title: "Spec 0007: kind filter and program cycle mark in the viewer's tree"
description: "How TIA Viewer's tree shows only blocks of chosen kinds, together with the name filter, and marks the OBs where the program's cycle starts."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: stable
---
# Spec 0007: kind filter and program cycle mark in the viewer's tree

## Intent

Someone reading a PLC program in TIA Viewer narrows the tree to the kinds of block they are after, such as only the OBs or only the data blocks and tag tables, alone or together with the name filter, and sees at a glance where the program starts: the program cycle OB, OB 1 `Main` in most programs, carries a mark of its own. Which OB that is comes from the event class the OB stores, not from its name, as [the format](../docs/FORMAT.md) describes in section 10.

## User scenarios

### Show only blocks of chosen kinds (P1)

- Given an opened archive, when the tree is shown, then above it, below the name filter, there is one switch per kind the tree holds, each with that kind's badge, and none is on.
- Given no kind switch is on, when the tree is shown, then it holds every kind, as before.
- Given the switch of one kind is turned on, when the tree is shown, then it holds only items of that kind, in their folders, and every folder that holds none is left out.
- Given the switches of several kinds are on, when the tree is shown, then it holds items of any of those kinds.
- Given a kind switch is on and a name is typed in the name filter, when the tree is shown, then it holds only items of a chosen kind whose name holds the text.
- Given a kind switch is on, when the tree is shown, then every folder left in it is expanded and its count counts what is left.
- Given a kind switch is turned off again and no other is on, when the tree is shown, then it holds every kind again.
- Given a kind switch is on, when another archive is opened, then every switch of the new archive is off.
- Given a kind switch is on, when a block of another kind is opened by following a name or from a list, then the block is shown, though the tree has no row for it.

### See where the program starts (P1)

- Given a PLC of the project with an OB whose stored event class is program cycle, when the tree is shown, then that OB's row carries a mark that says it is where the program's cycle starts.
- Given a PLC with several program cycle OBs, when the tree is shown, then each of them carries the mark.
- Given an OB with another event class, such as startup or cyclic interrupt, when the tree is shown, then it carries no mark, whatever its number.
- Given an OB that stores no event class, when the tree is shown, then it carries the mark if its number is 1, and no mark otherwise.
- Given a program cycle OB in a master copy, when the tree is shown, then it carries no mark: it is not the project's program.
- Given a closed program cycle OB, when the tree is shown, then it carries the mark as well as the lock.

## Requirements

- FR-001: The tree MUST offer one switch per kind of item it holds, each showing the kind's badge, in the order OB, FB, FC, DB, UDT, tag table, OPC UA, then any other kind in alphabetical order.
- FR-002: With no switch on, the tree MUST hold every item, as without the switches.
- FR-003: With one or more switches on, the tree MUST hold only the items of those kinds, and MUST leave out every folder that then holds none.
- FR-004: The kind filter and the name filter MUST both apply: an item is shown only if it passes both.
- FR-005: While either filter leaves something out, every folder in the tree MUST be expanded, and every count MUST count only what is shown.
- FR-006: Opening an archive MUST turn every switch off and empty the name filter.
- FR-007: A block opened by following a name, from a list or by Back or Forward MUST be shown even when the filters leave its row out of the tree.
- FR-008: A program cycle OB of the project itself MUST carry a mark in its tree row, with a tip that says it is where the program's cycle starts; no other row MUST carry it.
- FR-009: An OB MUST count as a program cycle OB when its stored event class is program cycle, and MUST NOT when it stores another event class; only an OB that stores none MUST count as one by having the number 1.
- FR-010: An OB in a master copy MUST NOT carry the mark.
- FR-011: The switches and the mark MUST work with Native AOT: no reflection-based bindings.

## Success criteria

- SC-001: In the demo project, a person finds every OB of a PLC with one click after opening the archive.
- SC-002: In the demo project and the samples it was checked on, the mark sits on OB 1 and on no other block.
- SC-003: Filtering a tree of a few thousand items by kind takes no longer than filtering it by name does today.

## Out of scope

- Filtering by language, by folder, or by closed or open.
- Remembering the chosen kinds between archives or sessions.
- Marking other entry points, such as startup or interrupt OBs, or showing an OB's event class in its heading.
- Filtering the lists below the source, or the search results, by kind.
- Telling which of several program cycle OBs runs first.

## Decisions

None new.
