---
type: Research note
title: "How TIA Portal shows an S7-1500's OPC UA server settings"
description: "The pages, labels and defaults TIA Portal uses for a CPU's OPC UA server, and where it shows OPC UA access of data blocks and tags, for laying out the viewer's OPC UA view."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
sources:
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=166
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=176
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=180
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=182
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=188
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=189
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=194
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=198
  - https://epics-modules.github.io/opcua/how-to/plc_configuration.html
---
# How TIA Portal shows an S7-1500's OPC UA server settings

The question: how TIA Portal lays out a CPU's OPC UA server, so that TIA Viewer can show what `tiadump opcua` reads in the shape an engineer knows. Main source: Siemens' S7-1500 function manual on OPC UA communication, edition 11/2019 (A5E03735815-AH), read through ManualsLib because Siemens' own download link answered 404. Printed page numbers below; ManualsLib numbers are one higher. The manual names no TIA Portal version, and later versions added settings (the AES policy below), so treat labels as a guide, not as TIA's current text.

## The pages

In the CPU's properties, under *OPC UA*:

| Page | What it holds | Manual |
|---|---|---|
| General | "Enable additional user administration via the security settings of the project" | p. 194 |
| Server › General | Activating the server, after confirming security notes; the server addresses, one per interface | p. 175, 177 |
| Server › Options | "TCP port for OPC UA" (4840, range 1024 to 49151); "Enable standard SIMATIC server interface"; subscriptions: "Minimum publishing interval", "Minimum sampling interval", "Maximum number of monitored elements" | p. 179-183 |
| Server › Security › Secure channel | The server certificate; security policies, one check box per line; "Trusted clients"; "Automatically accept all client certificates during runtime" | p. 184-189 |
| Server › Security › User authentication | "Enable guest authentication"; "Enable user name and password authentication" with a user list | p. 193-194 |
| Server › Diagnostics | Diagnostic settings of the server | p. 197 |
| Server › Export | Export of the server's XML description | p. 174 |
| Runtime licenses › OPC UA | The license the server needs | p. 175, 198 |

The EPICS guide shows the same tree as *General › OPC UA › Server › General / Options / Security* and *Runtime licenses*, and names the check box "Activate OPC UA server".

## Defaults

- The server is off: "not enabled for reasons of security" (p. 175).
- Port 4840; the standard SIMATIC server interface is on (p. 179).
- In this edition every security policy is on by default (p. 188). TIA lists them in this order: no security, Basic128Rsa15 sign, sign and encrypt, Basic256 sign, sign and encrypt, Basic256Sha256 sign, sign and encrypt; Basic256Sha256 is the recommended one (p. 188-189). The key tables of the samples name no Basic128Rsa15 key but one for a policy with RSA-PSS (`256RSA_PSS_SE`), which this edition does not mention: later TIA versions changed the list.
- The manual recommends user authentication over guest access (p. 193) and switching automatic acceptance of client certificates off after commissioning (p. 187).

## Data blocks and tags

- A data block's properties, area *Attributes*, hold the check box "DB accessible from OPC UA"; data blocks are open by default, and the program blocks overview has a column "Data block accessible via OPC UA" (p. 167-168).
- A PLC tag or data block member has the check box "Accessible from HMI/OPC UA", on by default (p. 165). TIA's tag table and block editors show it as a column. The writable and visible settings that a TIA source file writes as `ExternalWritable` and `ExternalVisible` are columns there too, but this edition of the manual does not name them, so their labels here are not confirmed.

## What it means for TiaTools

- The stored key suffixes of section 18 of the [format](../FORMAT.md) line up with this list: `None`, `256_S`/`256_SE` for Basic256, `256SHA_S`/`256SHA_SE` for Basic256Sha256, and `256RSA_PSS_SE` for the later policy with RSA-PSS. That is still inferred from names, not checked in TIA.
- A view that follows TIA's pages (General, Options, Security, User authentication) and its access columns for data blocks and tags will read familiar. A setting TiaTools has not found stored (port, guest access, user name and password) belongs on its page as "not stored", not left out.
