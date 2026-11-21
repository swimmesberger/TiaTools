---
type: Specification
title: "Spec 0009: LAD, FBD, STL and GRAPH blocks, read and checked against TIA Portal"
description: "The reader shows blocks in LAD, FBD, STL and GRAPH as readable text, and a reference project built and compiled by TIA Portal checks every one of them against TIA's own export."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T14:00:00Z }
status: stable
---
# Spec 0009: LAD, FBD, STL and GRAPH blocks, read and checked against TIA Portal

## Intent

A PLC program is often not all SCL: networks in LAD and FBD, older code in STL, and step sequences in GRAPH. Today the reader prints FBD networks in a layout of part calls that is hard to read and was never compared with TIA, and dumps STL and GRAPH blocks as the raw data TIA stores. Someone reading a program without TIA Portal, a person or a coding agent, should read every block's logic: STL as the source TIA itself generates for it, and LAD, FBD and GRAPH, which have no text form in TIA, in statement form, their logic written as statements (ADR 0013). The text the tools write, the dump, `tiadump show` and the viewer's export, is written first to be understood by a coding agent; the viewer may show these blocks in another way, as long as what it exports is that text. A new reference project, invented and built and compiled by TIA Portal, holds blocks of all four languages, and every test run checks the reader's text of each against TIA's own export of the same block, so the text says what TIA's editor shows, without TIA at hand.

## User scenarios

### Read a LAD or FBD block (P1)

- Given a block in LAD or FBD, when it is dumped, shown with `tiadump show` or exported from the viewer, then each network appears with its number, title and comment, followed by its logic in statement form.
- Given the viewer shows such a block in a view of its own, when the block is exported or copied from the viewer, then what is written is the statement form.
- Given a network of contacts in series and in parallel driving a coil, then its statement form is one assignment of the coil's operand from an expression of AND, OR and NOT over the contacts' operands, in the order of the network.
- Given a network with a set or reset coil, an edge contact or coil, a box such as a move, a comparison or arithmetic, a timer or counter, a call of another block, or a jump, then the statement form names the part, every operand it is wired to and every negated connection, and nothing that the network does not store.
- Given a network whose logic has no plain assignment, such as a box with an enable input, then the statement form still shows when it runs and what it is connected to.

### Read an STL block (P1)

- Given a block in STL, when it is dumped, shown or exported from the viewer, then its text is the source TIA Portal generates for it, by the rules of the comparison.

### Read a GRAPH block (P1)

- Given a block in GRAPH, when it is dumped, shown or exported from the viewer, then it shows each sequence with each step's number and name and its actions with their qualifiers, each transition's number and name and its condition in statement form, which step each transition leads to, the branches and jumps between them, and the block's permanent instructions before and after the sequence.
- Given a step with an interlock or a supervision condition, then it shows that condition in statement form.

### The tests check every language against TIA Portal (P1)

- Given the reference project for these languages, when the library's tests run on a machine without TIA Portal, then the text of each STL block is compared with TIA's generated source, and the statement form of each LAD, FBD and GRAPH block with the statement form of TIA's export of the same compiled block, read by the same rules.
- Given a block whose text differs from what TIA's export gives, when the tests run, then they fail and name the reference project, the block, the network or the step or transition, and both versions of what differs.
- Given the reference project's sources, when the maintainer builds it again, then TIA builds the project, compiles it, and exports what the comparison needs, and a block TIA refuses or fails to compile is reported with TIA's error and the committed files stay unchanged.

### The reference project covers each language's parts (P2)

- Given the reference project, then together its blocks hold: in LAD and in FBD, normally open and closed contacts in series and in parallel, assignment, set and reset coils, edge detection, a move, a comparison, an arithmetic box, a timer as a multi-instance and with its own instance DB, a counter, a call of an FC and of an FB with parameters, a negated input and output, a jump to a label, and a network title and comment; in STL, bit logic with nesting, load and transfer, arithmetic, a jump, a call with parameters, and a comment; in GRAPH, a linear sequence, an alternative branch, a simultaneous branch, a jump, actions with the qualifiers N, S, R, D and a call, an interlock, a supervision, and permanent instructions before and after the sequence.
- Given a LAD or FBD block with an SCL network and an STL network among its networks, then each network is shown in its own language.

## Requirements

- FR-001: The dump, `tiadump show` and the viewer's export and copy MUST show each network of a LAD or FBD block with its number, title and comment and its logic in statement form.
- FR-002: The statement form of a network MUST name every part, operand, negation and connection the network stores, and MUST NOT show anything it does not store.
- FR-003: Where a network's parts form plain logic, contacts and boxes of logic driving coils, the statement form MUST be assignments of AND, OR and NOT expressions; other parts MUST be shown as calls with every connected input and output named.
- FR-004: The reader MUST show an STL block as the source TIA Portal generates for it, by the rules `tiadump compare` uses.
- FR-005: The reader MUST show a GRAPH block's sequences with every step, action and qualifier, every transition with its condition in statement form, the step each transition leads to, branches and jumps, interlocks, supervisions, and the permanent instructions before and after the sequences.
- FR-006: A network of another language inside a LAD or FBD block MUST be shown in its own language: SCL as SCL, STL as STL.
- FR-007: A reference project MUST hold invented blocks in LAD, FBD, STL and GRAPH that together cover every part listed in the fifth user story, built and compiled by TIA Portal V21.
- FR-008: Building a reference project MUST also accept blocks in the form TIA exports them, for languages TIA takes no text source for, and MUST compile the project and export each compiled block of those languages, as TIA exports it, beside the generated source.
- FR-009: Building MUST report every block TIA refuses to import or fails to compile, with TIA's error, in one run, and MUST leave the committed files unchanged when there is any.
- FR-010: The library's tests MUST compare each STL block of the reference projects with TIA's generated source, and the statement form of each LAD, FBD and GRAPH block with the statement form of TIA's export of that block, and fail on a difference naming the reference project, the block, the network or step or transition, and both versions.
- FR-011: Search MUST find an operand used only in a LAD, FBD or GRAPH block, as it finds one used only in SCL.
- FR-012: A fail-safe network MUST be shown in statement form like any other, though no reference project checks it.
- FR-013: A know-how protected block in any of these languages MUST stay closed, as spec 0008 requires for every protected block.
- FR-014: The viewer MAY show LAD, FBD and GRAPH blocks in a view of its own, such as a drawing; until it has one, it MUST show the statement form.

## Success criteria

- SC-001: Every block in LAD, FBD, STL or GRAPH of the reference project is shown as readable text, with none dumped as raw stored data.
- SC-002: Every part listed in the fifth user story is in the reference project, and its text agrees with TIA's export in every test run.
- SC-003: After TIA Portal is no longer available, a change that breaks how the reader shows any of these languages fails the tests in CI.
- SC-004: A coding agent, or a person who knows SCL, can tell from the exported text of a network or sequence what it does, without TIA Portal.

## Out of scope

- Drawing LAD rungs, FBD boxes or GRAPH sequences in the viewer as TIA's editors do: allowed by FR-014, but a later spec of its own.
- Writing SimaticML or any other form TIA could import.
- CFC, ProDiag, and the languages of S7-300 and S7-400 projects that S7-1500 projects do not use.
- Checking fail-safe blocks against TIA, which needs the safety program's password.
- Comparing the FBD of the samples with TIA; the samples stay outside the repository.
- Instance DBs of GRAPH blocks beyond what spec 0008 already compares.
- A GRAPH step's maximum and warning times, which TIA's export gives but the reader has not found stored.

## Decisions

- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md): reference projects are committed.
- [ADR 0013](../docs/adr/0013-networks-are-shown-as-statements.md), new: the text the tools write is for a coding agent to understand, so LAD, FBD and GRAPH logic is written in statement form, a text of TiaTools' own; the viewer may show them otherwise.
