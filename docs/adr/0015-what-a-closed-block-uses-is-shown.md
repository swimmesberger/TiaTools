---
type: Architecture Decision Record
title: "What a closed block uses is shown; its body is not"
description: "TIA's cross-reference list keeps in clear what a know-how protected block calls, reads and writes; the reader shows which other objects a closed block uses and how, but never its body, the uses of its own members, or where inside it a use is."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# What a closed block uses is shown; its body is not

ADR 0011 has the reader show a closed block's name, kind, number, language, folder and what else the archive leaves in clear, and nothing of its encrypted body. TIA's cross-reference list, `XRef/XRef.db`, is not encrypted, and holds for a closed block the same rows as for an open one: the blocks it calls, the tags and data blocks it reads and writes, the members of its own interface it reads and writes, and for each the network and the UId of the part or token. The reader shows the first kind, which other objects a closed block uses and with what access, because someone reading the program needs to know what a closed block touches, and it says nothing about how the block works. It leaves out the second kind and every place inside the block, because together they would retrace the body's statements, which is what the protection is for.

## Considered options

- **Show what it uses, without places and own members** (chosen): the program's structure stays complete around a closed block; its logic stays closed.
- **Show nothing recorded inside a closed block**: the closest to ADR 0011's wording, but a closed block's writes to tags would stay unknown, as they are today with the references found in the text.
- **Show every row as stored**: the order and kind of the body's accesses, token by token, which comes close to the body itself.

## Consequences

The rule in `AGENTS.md`, the glossary's **Closed** and `docs/FORMAT.md` section 15 say that the reader renders nothing of a closed block's body, rather than nothing more. Decryption is unchanged: still unbuilt, and password-only if it is ever built (ADR 0011). A tool that reads `XRef.db` directly sees the full rows; this decision is about what TiaTools shows.
