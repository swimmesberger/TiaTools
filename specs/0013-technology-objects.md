---
type: Specification
title: "Spec 0013: Technology objects, read and checked against TIA Portal"
description: "The reader shows each technology object of a PLC, axes, PID controllers, counters, measuring inputs and output cams, with its kind, version and parameters, and a reference project built by TIA Portal checks every parameter value of every kind Openness can create against TIA's own."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T16:00:00Z }
status: stable
---
# Spec 0013: Technology objects, read and checked against TIA Portal

## Intent

Much of what a machine does is not in its blocks but in its technology objects: an axis's limits, dynamics and mechanics, a PID controller's ranges and tuning, a counter's limits. TIA stores each as a data block of a technology type, which the reader skips today, so the dump, `tiadump list` and the viewer show nothing of them, and a block that calls `MC_MoveAbsolute` on an axis points at something the reader cannot show. Someone reading a program without TIA Portal, a person or a coding agent, should see each technology object of a PLC: its name, kind, version and number, and its parameters as TIA's parameter view names them, each with the value TIA shows, whether set on the object or left at its default. A reference project, built by TIA Portal from invented sources with parameters set away from their defaults, holds every kind TIA Portal's Openness can create, and every test run checks the reader's value of each parameter against the value TIA reported when it built the project.

## User scenarios

### See a PLC's technology objects (P1)

- Given an archive whose PLC has technology objects, when it is listed with `tiadump list`, dumped, or opened in the viewer, then each technology object appears with its name, kind, version, number and folder, apart from the PLC's blocks.
- Given a technology object's name, when it is shown with `tiadump show`, then its kind, version, number and parameters are printed.
- Given a technology object of a kind no reference project holds, then it is still shown with its name, kind, version and number, and with its parameters by the same rules as the others.

### Read a technology object's configuration (P1)

- Given an axis whose maximum velocity was set to a value of its own, when it is dumped, then the dump shows that parameter under the name TIA's parameter view gives it, `DynamicLimits.MaxVelocity`, with that value.
- Given a parameter left at its default, then the dump shows the default value TIA shows for it.
- Given a positioning or synchronous axis, which TIA builds on the speed axis, then its inherited parameters carry the names TIA gives them, without a prefix that TIA does not show.
- Given a technology object, then its dump holds every parameter TIA lists, among the other members of its data block.

### Technology objects under Siemens' protection (P1)

- Given a PID controller, a high-speed counter or an SSI encoder, which TIA marks as protected by Siemens while it stores their parameters in clear and shows them in its parameter view without a password, when it is dumped, then its parameters are shown like those of any other technology object.
- Given a technology object any part of which is stored encrypted, then that part stays closed and is reported as such.

### The tests check every parameter against TIA Portal (P1)

- Given the reference project for technology objects, when the library's tests run on a machine without TIA Portal, then each parameter TIA reported for each technology object, where it is a member of the object's data block, is compared with the reader's value of that parameter.
- Given a parameter whose value the reader gives differently, or does not give, when the tests run, then they fail and name the reference project, the technology object, the parameter, and both values.
- Given the reference project's sources, when the maintainer builds it again, then TIA creates each technology object in its folder, sets each parameter the sources give, compiles, and writes the value of every parameter of every technology object as TIA reports it, without a person working in TIA; a technology object or parameter TIA refuses is reported with TIA's error, and the committed files stay unchanged.

### The reference project covers every kind (P2)

- Given the reference project, then it holds at least one of each: speed axis, positioning axis, synchronous axis, external encoder, PID_Compact, PID_3Step, PID_Temp, high-speed counter and SSI absolute encoder, each with at least one parameter set away from its default, and technology objects in a folder of their own.

### Find a technology object (P2)

- Given a technology object's name, when it is searched for in the viewer, then the technology object is found.
- Given a block whose code names a technology object, such as an axis passed to a motion instruction, when the reader resolves that name, then it leads to the technology object.

## Requirements

- FR-001: `tiadump list`, the dump, `tiadump show` and the viewer MUST show every technology object of a PLC with its name, kind, version, number and folder, apart from its blocks.
- FR-002: The reader MUST show each parameter of a technology object under the name TIA's parameter view gives it, with the value TIA shows: the value set on the object, else its default.
- FR-003: For every kind the reference project holds, the reader MUST show every parameter TIA reports for the object that is a member of its data block, and MAY show the data block's other members: which members TIA reports is a list of its own per kind that nothing stored gives (`docs/FORMAT.md`, section 19).
- FR-004: A technology object of a kind the reader has not been checked on MUST be shown by the same rules, and MUST NOT make reading the archive fail.
- FR-005: A technology object that TIA marks as protected by Siemens MUST be shown with every name and value stored in clear; a part stored encrypted MUST stay closed and be reported, as for any protected block.
- FR-006: A reference project MUST hold invented technology objects of every kind the fifth user story lists, each with at least one parameter set away from its default, built and compiled by TIA Portal V21.
- FR-007: Building a reference project MUST create its technology objects and set their parameters from its sources without a person working in TIA, and MUST write, for every technology object, its kind, its version and the value TIA reports for every parameter.
- FR-008: Building MUST report every technology object or parameter TIA refuses, with TIA's error, in one run, and MUST leave the committed files unchanged when there is any. A compile error that only says a technology object has no hardware assigned MAY be accepted, where the sources name it.
- FR-012: A parameter whose value is in neither the object nor a compiled image, as in an object TIA never compiled, MUST be shown without a value, never with a guessed one.
- FR-009: The library's tests MUST compare every parameter the build wrote, that is a member of the object's data block, with the reader's value, and fail on a difference naming the reference project, the technology object, the parameter and both values. A difference the reference project lists as known, with its reason, passes while it still differs.
- FR-010: Search in the viewer MUST find a technology object by its name.
- FR-011: Resolving a name in a block's code SHOULD lead to the technology object of that name.

## Success criteria

- SC-001: Every technology object of the reference project is listed, and every parameter TIA reports for it in its data block agrees with TIA's value in every test run.
- SC-002: After TIA Portal is no longer available, a change that breaks how the reader shows a technology object fails the tests in CI.
- SC-003: A coding agent can answer what an axis's software limits or maximum velocity are, or what range a PID controller's input has, from the dump alone.

## Out of scope

- A technology object's hardware connection: the drive and encoder telegrams, I/O addresses, and the module and channel of a counter. TIA keeps these outside the object's data block; its names for them start with `_`. The hardware is spec 0010's.
- Checking measuring inputs, output cams and cam tracks against TIA: Openness V21 can neither create nor import them, so no reference project can be built with them without a person working in TIA. The reader shows them by the rules every kind follows (FR-004).
- Cams (`TO_Cam`), kinematics, the interpreter, the leading axis proxy, and the cam profiles and point lists of any object.
- Technology objects of S7-1200 and of earlier CPUs, beyond what the same rules give: no reference project checks them.
- The alarms of technology objects, which are texts and alarms (spec 0014).
- Online and runtime values, and the commissioning and tuning results a PLC stores at runtime.
- Writing technology objects, or any form TIA could import.

## Decisions

- [ADR 0011](../docs/adr/0011-know-how-protection-stays-closed.md): protected blocks are reported as encrypted.
- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md): reference projects are committed.
- [ADR 0016](../docs/adr/0016-system-protected-technology-objects-are-read.md), new: a technology object under Siemens' system protection is shown as far as it is stored in clear, because only ciphertext is closed.
