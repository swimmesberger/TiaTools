---
type: Specification
title: "Spec 0004: OPC UA report"
description: "What tiadump opcua must report about each PLC's OPC UA server, roles and external access, and what it must never show."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
---
# Spec 0004: OPC UA report

## Intent

Someone who wants to read a PLC over OPC UA, and has only the project's archive, learns from it whether each PLC's own OPC UA server is on, how it is set up, which roles it has, and which data blocks, members and tags an OPC UA client could read or write. Today that needs a walk over every object and a grep through hundreds of megabytes. The report is text a person reads and JSON a program reads, and it never shows a password, key, hash or certificate.

## User scenarios

### Is the OPC UA server on, and how is it set up (P1)

- Given a PLC whose OPC UA device item stores the server as enabled, with an application name, security policy switches, client certificate acceptance and a minimum publishing interval, when the report is made, then it says the server is on and gives each of these values.
- Given a PLC whose OPC UA device item stores a port, guest authentication, user name and password authentication and the standard server interface, and whose PLC stores a minimum sampling interval, when the report is made, then it gives each of these values.
- Given a PLC whose OPC UA device item stores no enable switch, when the report is made, then it says the server is off.
- Given a PLC with no OPC UA device item, when the report is made, then it says so.
- Given the device item stores a setting the report does not name, when the report is made, then the setting is listed with its stored name and value.

### What could an OPC UA client reach (P1)

- Given a data block closed to OPC UA and one that stores no such setting, when the report is made, then the first is reported closed and the second open.
- Given a data block with a member closed to outside access, one read-only, and a closed struct with members, when the report is made, then the closed and the read-only member are named, and the struct's members are not named again.
- Given a tag table with a tag that is accessible, one read-only, one not accessible and a constant, when the report is made, then the three tags are reported with that access and the constant is left out.
- Given the server is off, when the report is made, then it says that no client reaches the PLC through its own server, and still says what would be open.

### Which server interfaces and roles exist (P2)

- Given a PLC's OPC UA storage with roles, when the report is made, then each role is listed with its name, its project role, whether it was imported, and its permissions per namespace.
- Given a PLC's OPC UA storage with empty journals, no server interface objects and a compile date, when the report is made, then it says there are no interfaces and no changes, and gives the date.
- Given a PLC with server interface objects, when the report is made, then their names are listed.

### Nothing secret comes out (P1)

- Given a device item that stores passwords and certificate ids, when the report is made, then neither their values nor their names appear in it, and the certificates are reported only as assigned and counted.
- Given an object of user management, when it is inspected, then its values are not shown.
- Given an object with an expando value named like a password, when it is inspected, then that value is not shown.

### Read it in a program (P2)

- Given any archive, when the report is asked for as JSON, then it holds the same facts as the text report, every data block and tag included.

### Find where a fact lives (P3)

- Given an object with dictionary expando values, when it is inspected, then each value is shown with its key.

## Requirements

- FR-001: `tiadump opcua <archive>` MUST write the report for every PLC of the project itself, leaving out PLCs held only in master copies.
- FR-002: The report MUST give, per PLC, whether its OPC UA server is enabled; a missing enable switch MUST count as off.
- FR-003: The report MUST give the application name, each security policy switch by its stored key, whether client certificates are accepted automatically, the port, whether guest and user name and password authentication are on, whether the standard SIMATIC server interface is on, and the minimum publishing and sampling intervals, and MUST mark each one that is not stored as not stored and say what TIA's defaults are.
- FR-004: The report MUST list every other setting of the device item by its stored name and value, except settings whose name or type the secret rule (FR-012) withholds.
- FR-005: The report MUST give, per PLC, the last compile and download dates of its server interfaces, whether the structure and mapping journals hold changes, and the names of its server interface objects.
- FR-006: The report MUST list the PLC's OPC UA roles with name, project role, whether imported, and permissions per namespace, and MUST say so instead of failing when the roles cannot be read.
- FR-007: The report MUST give, per data block of the PLC, whether it is open to OPC UA, a missing setting counting as open, and whether it is know-how protected.
- FR-008: The report MUST name each member of an open data block that is closed to outside access or read-only, and MUST NOT name again a member under one already named.
- FR-009: The report MUST give, per tag of the PLC's tag tables, whether it is accessible and writable, and MUST leave out constants.
- FR-010: The report MUST say which of its readings are not yet confirmed against TIA: the names of the RSA-PSS and RSA-OAEP policy keys, and what TIA makes of a tag stored as not accessible but visible.
- FR-011: `tiadump opcua --json` MUST write the same report as JSON.
- FR-012: The report and `tiadump inspect` MUST NOT show a value whose name holds password, secret, private, credential, encrypt, hash, salt, certificate, thumbprint, pki or key, or any value of a user management or certificate object.
- FR-013: Certificates MUST be reported only as assigned or not, and as a count.
- FR-014: `tiadump inspect` MUST show an object's dictionary expando values with their keys.
- FR-015: Reading the OPC UA configuration MUST NOT change what `tiadump dump` writes.
- FR-016: `tiadump opcua --plc <name>` MUST limit the report to that PLC and exit with 1 if there is none.

## Success criteria

- SC-001: On each of the three archives it was checked on, the report states for every PLC whether its server is on, in under two seconds.
- SC-002: A person who wants to read a PLC over OPC UA can tell from the report alone which data blocks and tags to ask about, without walking objects.
- SC-003: No report or inspection of the archives it was checked on contains a password, hash, key or certificate.

## Out of scope

- Decoding the compiled server interface, the companion specifications and the nodes of a server interface: the samples have none to check against.
- The project's users and their passwords, which live in user management and stay unread.
- OPC UA client settings of a PLC, and the OPC UA server of an HMI.
- Technology object data blocks: the reader does not list them as blocks.
- Writing the report into a dump.

## Decisions

None new. The reader takes no package (`docs/architecture.md`), so the serialized roles are read by its own reader of the documented binary format.
