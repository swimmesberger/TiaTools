---
type: Specification
title: "Spec 0016: GRAPH step times, more STL, ProDiag assignments and classic CPUs"
description: "The program-side gaps spec 0009 left: a GRAPH step's maximum and warning times, STL with indirect addressing and the forms StlBasics does not use, which ProDiag function block evaluates a data block, and the blocks of an S7-300 CPU, each checked against TIA Portal on a new reference project."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T16:00:00Z }
status: stable
---
# Spec 0016: GRAPH step times, more STL, ProDiag assignments and classic CPUs

## Intent

Spec 0009 made every LAD, FBD, STL and GRAPH block readable and checked each language against TIA Portal, but left parts of a PLC program out: a GRAPH step's maximum and warning times, STL beyond the basics (indirect addressing above all, which older code is full of), ProDiag, which says what a machine watches and reports, and the blocks of S7-300 and S7-400 CPUs, which many plants still run. Someone reading a program without TIA Portal, a person or a coding agent, should see all of these, written for a coding agent first as ADR 0013 asks. A new reference project, invented and built by TIA Portal V21, holds each of them, and every test run checks the reader against TIA's own generated source or export of the same blocks.

The parts come in this order, each usable on its own: GRAPH step times, STL, ProDiag, classic CPUs. TIA Portal is at hand only for a while, so one build of the reference project holds the blocks of all four, and a part TIA Portal V21 cannot build is dropped from this spec with the reason, rather than read without a check. Two were dropped. CFC: the TIA Portal V21 at hand has no CFC editor installed, and Openness offers no form in which a CFC chart can be imported or exported. ProDiag supervisions on members of a function block or PLC data type: Openness V21 refuses every SimaticML that holds one, in a software unit and outside one, and has no other way to create them. What remains of ProDiag is which ProDiag function block evaluates a data block, which TIA builds and exports.

## User scenarios

### Read a GRAPH step's times (P1)

- Given a GRAPH block whose step has a maximum step time and a warning time, when it is dumped, shown with `tiadump show` or exported from the viewer, then the step's line in the statement form is followed by both times, as TIA's export of the block gives them.
- Given a step whose times were never changed, then it shows the times TIA's export gives for it, which are TIA's defaults.
- Given the reference project, then the times of every step of its GRAPH blocks equal those of TIA's export, and at least one step has times other than the defaults.

### Read STL with indirect addressing and the other STL forms (P1)

- Given an STL block that addresses memory indirectly, through a pointer in a variable or through an address register, when it is dumped, shown or exported, then its text is the source TIA Portal generates for it, by the rules of the comparison.
- Given an STL block with the forms the reference project lists in the fifth story's STL part, then its text is the source TIA generates for it.
- Given an STL statement the reader does not know, then it is still shown, as stored, and the dump does not fail.

### See which ProDiag function block evaluates a data block (P2)

- Given a global data block whose members of a PLC data type are assigned to a ProDiag function block, when it is dumped, shown or exported, then its text is followed by each assigned member with its ProDiag function block, in a form of TiaTools' own that does not change the block's text.
- Given an instance data block assigned to a ProDiag function block, then its text is followed by that function block.
- Given the OB TIA adds to call ProDiag function blocks, then it shows each call with its instance data block and function block.
- Given the reference project, when the library's tests run, then every assignment the reader shows equals what TIA's export of the same data block gives, and an assignment one side has and the other does not fails the tests.

### Read the blocks of a classic CPU (P3)

- Given a project with an S7-300 CPU, when it is dumped, listed, shown or exported, then each of its blocks in SCL, STL, LAD and FBD, its data blocks, PLC data types and tag tables are shown as for an S7-1500 CPU.
- Given such blocks, when the library's tests run, then the text of each block TIA generates source for equals that source, and the statement form of each LAD and FBD block equals the statement form of TIA's export, by the rules of spec 0009.
- Given a block of a classic CPU that the reader cannot read, then it is listed and reported as not read, and the rest of the project is read.

### The reference project covers each part (P2)

- Given the reference project, then together its blocks hold: a GRAPH step with a maximum step time and a warning time set, one with only the maximum set and a step without; in STL, memory-indirect addressing through a pointer variable, register-indirect addressing within and across areas, loading an address into an address register, opening a data block through a variable, absolute addresses in inputs, outputs and bit memory, word logic, shifts and rotates, conversions, a jump list, a loop, status bits, edge detection, set and reset, a conditional block end, calls of a function block with an instance data block and as a multi-instance, and constants of every format STL loads; a ProDiag function block, a global data block with a member and two array elements of a PLC data type assigned to it, and an instance data block assigned to it; and an S7-300 CPU with an SCL, an STL, a LAD and an FBD block, a timer and a counter of the classic kind, a shared data block, an instance data block, a PLC data type and a tag table.
- Given a part TIA Portal V21 cannot build, then it is not in the reference project, and this spec says so and why.

## Requirements

- FR-001: The statement form of a GRAPH step MUST show its maximum step time and its warning time, as TIA's export of the block gives them.
- FR-002: The library's tests MUST compare the times of every step of every GRAPH block of the reference projects with TIA's export, as part of the GRAPH comparison of spec 0009.
- FR-003: The reader MUST show an STL block with indirect addressing, and with every STL form the reference project holds, as the source TIA Portal generates for it, by the rules `tiadump compare` uses.
- FR-004: An STL statement the reader does not recognise MUST be shown as stored, and MUST NOT stop the block or the dump.
- FR-005: The reader MUST show which ProDiag function block each member of a global data block is assigned to, array elements by their index, and which one a whole instance data block is assigned to.
- FR-006: The reader MUST show the calls of the OB TIA adds for ProDiag function blocks, each with its instance data block and function block.
- FR-007: Assignments MUST be shown after the data block's text, and MUST NOT change the text that is compared with TIA's generated source.
- FR-008: The library's tests MUST compare every assignment of the reference projects with TIA's export of the same data block, and fail on a difference or on an assignment only one side has, naming the reference project, the data block and the assignment.
- FR-009: The reader MUST read the blocks, data blocks, PLC data types and tag tables of an S7-300 CPU as it reads an S7-1500 CPU's, and the library's tests MUST compare them with TIA's generated source and export as for an S7-1500 CPU.
- FR-010: A block of a classic CPU the reader cannot read MUST be listed and reported as not read, and MUST NOT stop the dump.
- FR-011: A reference project MUST hold invented blocks with every part listed in the fifth user story, built and compiled by TIA Portal V21, and MUST leave the existing reference projects unchanged.
- FR-012: Building a reference project MUST be able to give each PLC a CPU of its own, so that one project holds an S7-1500 and an S7-300 CPU.
- FR-013: Building MUST export, for every data block assigned to a ProDiag function block, its SimaticML beside its generated source.
- FR-014: Search MUST find an operand used only in an STL statement with indirect addressing, as it finds one used in SCL.
- FR-015: A know-how protected block of any kind in this spec MUST stay closed, as spec 0008 requires for every protected block.
- FR-016: The viewer MUST show assignments and step times with the block, as the text the tools write; it MAY show them in a view of their own as well.

## Success criteria

- SC-001: Every step time, STL block, ProDiag assignment and classic CPU block of the reference project is shown as readable text, and agrees with TIA in every test run.
- SC-002: After TIA Portal is no longer available, a change that breaks how the reader shows any of them fails the tests in CI.
- SC-003: A coding agent, or a person who knows SCL, can tell from the text what a step's times are, what an STL block with indirect addressing does, and which ProDiag function block evaluates which data, without TIA Portal.
- SC-004: Each part this spec drops is named here with the reason.

## Out of scope

- CFC: not installed with the TIA Portal V21 at hand, and Openness can neither import nor export a chart, so nothing could check the reader. A later spec, when a TIA with CFC is at hand.
- ProDiag supervisions on members of a function block or PLC data type, and the supervisions a ProDiag function block evaluates: TIA Portal V21's Openness refuses SimaticML with supervisions ("The namespace '' is incorrect"), in a software unit or not, and offers no other way to create them, so no reference project can hold one. A later spec, when a TIA version imports them or a person builds one by hand.
- ProDiag function blocks' and their OB's own text beyond their interface and calls: TIA neither generates source for them nor exports them.
- GRAPH on an S7-300 or S7-400 CPU, whose stored form differs from the GRAPH of an S7-1500.
- An S7-400 CPU, unless the S7-300 part shows that its blocks are stored differently from an S7-1500's; then a later spec.
- The alarm texts of supervisions and the PLC's supervision settings (categories and their texts), which belong with the texts and alarms of the project.
- ProDiag's runtime: the supervision state structures, the ProDiag overview and the alarms an HMI shows.
- Fail-safe blocks, which need the safety program's password.
- Drawing GRAPH sequences in the viewer.

## Decisions

- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md): reference projects are committed.
- [ADR 0013](../docs/adr/0013-networks-are-shown-as-statements.md): the text the tools write is for a coding agent; assignments and step times follow it.
- [ADR 0011](../docs/adr/0011-know-how-protection-stays-closed.md): protected blocks stay closed.
