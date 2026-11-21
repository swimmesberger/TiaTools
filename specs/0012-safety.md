---
type: Specification
title: "Spec 0012: safety report, read and checked against TIA Portal"
description: "The reader and the viewer report what a project stores about each PLC's safety program: its settings, F-runtime groups, collective and block F-signatures, and the F-parameters of its F-I/O, never a safety password, and a reference project built by TIA Portal checks it against what TIA reports through Openness."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# Spec 0012: safety report, read and checked against TIA Portal

## Intent

The reader already shows the logic of fail-safe blocks, but nothing around it: whether a PLC has a safety program at all, how it is split into F-runtime groups, which F-OB calls each and how often, its maximum cycle time, which collective F-signature the program was last compiled to, the F-signature of each F-block, and the F-source address, F-destination address and F-monitoring time of each F-I/O. Someone who checks a machine's safety program against its acceptance documents, or who wants to know whether two archives hold the same safety program, needs exactly these values, and today needs TIA Portal and STEP 7 Safety to see them. `tiadump safety` writes a safety report per PLC from the archive alone, as text and as JSON, and the viewer shows it beside the PLC's program. It reports only what the archive stores in clear: a safety password is never looked for, shown or used, and whether one is set is all it says about it. A new reference project with a safety program, invented and built by TIA Portal, checks every value of the report against what TIA reports for the same project through Openness, in every test run.

## User scenarios

### See a PLC's safety program at a glance (P1)

- Given an archive with a PLC that has a safety program, when `tiadump safety` runs on it, then it reports for that PLC the safety system version, whether the F-change history is on, whether safety mode can be disabled, how F-block numbers are assigned with the ranges where they are fixed, and whether a safety program password is set.
- Given a PLC with no safety program, then the report says so for that PLC.
- Given an archive with no PLC, such as a global library, then the report says so and the command exits without error.
- Given `--plc`, then only that PLC is reported; given a name no PLC has, the command fails and says so.
- Given `--json`, then the same content is written as one JSON document.

### See the F-runtime groups (P1)

- Given a safety program with two F-runtime groups, then each is reported with its name, its F-OB with name, number, event class and, for a cyclic interrupt, its cycle time, its main safety block and that block's instance data block, its maximum cycle time and its warning limit of the cycle time.
- Given an F-runtime group with an F-runtime group information DB, or a pre- or post-processing block, then they are reported with it.

### See the F-signatures (P1)

- Given a safety program that TIA compiled, then the report gives its collective F-signature, its collective F-SW signature, its collective F-HW signature and its F-communication address signature, and when it was compiled.
- Given the fail-safe blocks of the program, then each is reported with its kind, number, name and F-signature, those TIA generated marked as such.
- Given a fail-safe block that is know-how protected, then its F-signature is still reported, and nothing of its body.
- Given a safety program that was never compiled, then the report says there is no collective F-signature, rather than giving a value.

### See the F-parameters of the F-I/O (P1)

- Given an F-I/O, central in the F-CPU's rack or in an IO device, then it is reported under the name of its device and its own name, with its first input and output address, its F-source address, its F-destination address, its F-monitoring time and its F-parameter signature.
- Given an F-I/O whose F-monitoring time was set by hand and one that takes the F-CPU's default, then both are reported with the time TIA uses, the first marked as set by hand.
- Given an F-I/O, then the number and name of its F-I/O data block are reported with it.
- Given the F-CPU, then its central F-source address, its range of F-destination addresses and its default F-monitoring time are reported, the values TIA uses when the archive stores none.

### Nothing secret comes out (P1)

- Given a safety program with a password set, when the report is made, then it says a password is set, and no password, hash, salt or other value derived from it appears in the report, in either form.
- Given a safety program with a password set, then everything the other scenarios name is reported as it is for one without.
- Given a value the archive stores encrypted, then it is reported as closed.

### The tests check the report against TIA Portal (P1)

- Given the safety reference project, when the library's tests run on a machine without TIA Portal, then every value of the safety report is compared with what TIA reported for the same project through Openness when it built it.
- Given a difference, then the tests fail and name the reference project, the PLC, the object (runtime group, block, F-I/O) and the value, with both versions.
- Given something TIA reported that the report leaves out, or the other way round, then the tests fail and name it.
- Given the fail-safe blocks of the reference project, then their networks in statement form are compared with TIA's SimaticML export of them, as for LAD and FBD.

### The reference project covers a safety program's parts (P2)

- Given the safety reference project, then it holds an F-CPU with an F-DI and an F-DQ in its rack, an IO device with an F-DI on PROFINET, one F-I/O with its F-monitoring time set by hand, two F-runtime groups with cycle times other than TIA's defaults, F-blocks in F-FBD and F-LAD, an F-FB, an F-FC and a fail-safe global DB, and a safety program that TIA compiled.
- Given the safety reference project, then it holds a second PLC whose compiled safety program has an invented password set, so the tests check on an archive TIA wrote that the password stays out of every output.

### See the safety report in the viewer (P2)

- Given an archive with a PLC of the project that has a safety program, when it is opened, then the PLC's node in the tree holds an entry "Safety", after "OPC UA".
- Given a PLC without a safety program, or one held only in a master copy, then no such entry is shown for it.
- Given the entry is chosen, when it is shown, then the view shows the safety program's settings and signatures, its F-runtime groups, its fail-safe blocks with their F-signatures, and the F-CPU's and each F-I/O's F-parameters, read-only, with the labels of TIA's safety administration.
- Given a value the archive does not store, then it reads as TIA's default, marked as such.
- Given a fail-safe block's row, or the F-OB or main safety block of an F-runtime group, when it is opened, then the viewer shows that block, and Back returns to the view.
- Given the view is shown, when Copy is chosen, then the text `tiadump safety --plc <PLC>` writes is copied.
- Given text that occurs only in the safety report, when all source is searched, then nothing is found; given a PLC or the project is exported, then the files are the same as without the entry, and the entry offers no export.

## Requirements

- FR-001: `tiadump safety <archive>` MUST report, for each PLC of the project, whether it has a safety program.
- FR-002: For a PLC with a safety program it MUST report the safety system version, whether the F-change history is on, whether safety mode can be disabled, the mode of F-block number assignment and, where the numbers are fixed, the ranges for F-FBs, F-FCs and F-DBs.
- FR-003: It MUST report whether a safety program password is set, and MUST NOT show any password, hash, salt or value derived from one, nor anything the archive stores encrypted, which it reports as closed.
- FR-004: It MUST report each F-runtime group with its name, its F-OB (name, number, event class, and the cycle time of a cyclic interrupt), its main safety block and that block's instance data block, its maximum cycle time and its warning limit.
- FR-005: It SHOULD report each F-runtime group's F-runtime group information DB and its pre- and post-processing blocks, where the program has them.
- FR-006: It MUST report the collective F-signature, the collective F-SW signature, the collective F-HW signature and the F-communication address signature of the safety program, and when it was last compiled; for a program never compiled it MUST report no signature.
- FR-007: It MUST report each fail-safe block with its kind, number, name and F-signature, and mark the blocks TIA generated.
- FR-008: It SHOULD write F-signatures as TIA's safety administration shows them, in hexadecimal.
- FR-009: It MUST report each F-I/O by its device's name and its own name, with its first input and output address, F-source address, F-destination address and F-monitoring time, as the values TIA uses.
- FR-010: It MUST report each F-I/O's F-parameter signature and whether its F-monitoring time was set by hand, and SHOULD report the number and name of its F-I/O data block.
- FR-011: It MUST report the F-CPU's central F-source address, the lower and upper limit of its F-destination addresses and its default F-monitoring time; where the archive stores none of them, the value TIA uses, marked as the default.
- FR-012: `--json` MUST write the same content as the text, as one JSON document; `--plc` MUST limit the report to one PLC and fail on a name no PLC has.
- FR-013: An archive without PLCs MUST give an empty report and exit 0.
- FR-014: A reference project MUST hold an invented safety program covering every part listed in the seventh user story, built and compiled by TIA Portal V21 from sources committed beside it.
- FR-015: Building a reference project with a safety program MUST also write what TIA reports about it through Openness beside the generated source: the safety settings, the F-runtime groups, the signatures of the program and of each fail-safe block, and the F-parameters of the F-CPU and of each F-I/O.
- FR-016: The library's tests MUST compare the safety report of each reference project that holds TIA's report of its safety program with that report, and fail on a difference, naming the reference project, the PLC, the object, the value and both versions; a value reported by one side only MUST fail too.
- FR-017: The library's tests MUST compare the statement form of the reference project's fail-safe blocks with TIA's SimaticML export of them.
- FR-018: `docs/FORMAT.md` MUST describe where the safety program's settings, F-runtime groups, signatures and F-parameters are stored, with each claim marked as the format page marks claims.
- FR-019: Building the reference project MUST NOT look for, guess, decrypt or log in with a safety password; the second PLC's password MUST be invented, written in the exporter, and set only after its safety program compiled.
- FR-020: The tree MUST hold one entry "Safety", after "OPC UA", under each PLC of the project itself that has a safety program, and none under any other PLC.
- FR-021: Choosing the entry MUST show a structured, read-only view of that PLC's safety report, read when first shown, not when the archive is opened, with every value FR-002 to FR-011 name and the labels of TIA's safety administration; a default MUST be marked as such.
- FR-022: Opening a fail-safe block's row, or an F-runtime group's F-OB or main safety block, MUST show that block, as following a name in the source does.
- FR-023: Copy MUST copy the text `tiadump safety --plc <PLC>` writes.
- FR-024: The safety report MUST NOT count as an item of the project: it MUST NOT be searched, referenced, counted in the status line, or exported, and the entry MUST NOT offer an export.
- FR-025: The view MUST NOT show a password or anything derived from one, as FR-003 says.

## Success criteria

- SC-001: For the safety reference project, every setting, F-runtime group, signature and F-parameter TIA reports agrees with the safety report.
- SC-002: Someone with only two archives of the same machine can tell whether their safety programs are the same, by comparing collective F-signatures, and which F-blocks differ.
- SC-003: Someone with only an archive can read off, for each F-I/O, the F-destination address to set on its switches and its F-monitoring time.
- SC-004: No safety password, nor anything derived from one, appears in any output of TiaTools for an archive that has one.
- SC-005: After TIA Portal is no longer available, a change that breaks how the reader reads any covered part of the safety program fails the tests in CI.

## Out of scope

- Logging in to a safety program, its password, and anything only a logged-in user can see; decryption of any kind (ADR 0011).
- Channel parameters of F-I/O: sensor supply, discrepancy behaviour and time, input delay, short-circuit and light tests, passivation behaviour.
- The module structure of the hardware: devices, racks, slots, order numbers, subnets and addresses other than an F-I/O's first input and output address belong to spec 0010; the safety report names an F-I/O as spec 0010's device list does, so the two can be read together.
- Online data: the F-signatures of the program loaded in a CPU, the F-change history's entries, acceptance and printouts.
- F-communication between PLCs (Flexible F-Link, I-device, safety over OPC UA) and safety-related shared devices.
- Fail-safe HMI and F-runtime groups in software units.
- Safety programs of S7-300, S7-400 and ET 200 CPUs, which no sample or reference project holds.
- Writing anything back to a project.

## Decisions

- [ADR 0011](../docs/adr/0011-know-how-protection-stays-closed.md): encrypted parts stay closed; a safety password is never looked for or used.
- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md): reference projects are committed with what TIA generated for them; the safety reference project adds TIA's report of its safety program.
