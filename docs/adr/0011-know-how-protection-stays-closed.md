---
type: Architecture Decision Record
title: "Protected blocks are reported as encrypted; decryption is unbuilt and password-only"
description: "Reading the format reports a know-how protected block as encrypted because the archive holds only ciphertext; decryption is a separate, unbuilt capability that, if added, would take a password the user supplies and never guess, brute-force or store."
generated: { by: claude-code/claude-opus-4-8, at: 2026-11-21T11:00:00Z }
status: stable
sources:
  - ../sources/2026-11-21-tia-know-how-protection-no-backdoor.md
  - https://industrialmonitordirect.com/blogs/knowledgebase/siemens-tia-portal-know-how-protection-removal-methods
---
# Protected blocks are reported as encrypted; decryption is unbuilt and password-only

The reader renders each compile unit in the form the archive stores it. A know-how protected block stores its code as a W3C XML Encryption envelope (`aes128-cbc`) and nothing else: no key, and no `KeyInfo` describing one, so there is no plaintext in the archive to render, and the reader reports it as encrypted exactly as it handles any other stored form. Decryption is a separate capability, not part of reading the format: the encryption is genuine with no backdoor and the key is tied to the password (the maintainer's trusted [source](../sources/2026-11-21-tia-know-how-protection-no-backdoor.md), consistent with the empty `KeyInfo`), and the password-to-key derivation lives only in TIA Portal, not in the archive. We have not built that capability, and if we do it will take a password the user supplies and never guess, brute-force or store one.

## Considered options

- **Report protected blocks as encrypted, do not decrypt** (what the reader does today): this is reading the format and nothing more — an encrypted envelope has no plaintext to show.
- **Build decryption with the user's own password** (a possibility, not yet specified): legitimate, since with the password the user could open the block in TIA Portal anyway and the tool's point is not needing TIA Portal. The cost is that the archive does not record the key derivation, so building it means reconstructing Siemens' password-to-key scheme; for a *published* tool that reverse-engineering carries a legal question under the EU Software Directive's circumvention provisions (not reviewed by a lawyer; flagged, not settled). It would need its own spec and ADR before any code.
- **Open protected blocks without the password** (not available): there is no backdoor and the key is tied to the password, so there is nothing here to build — not a rule we keep, a thing that does not exist.

## Consequences

Encrypted source stays unrendered; the reader shows whatever the archive leaves in clear (name, kind, number, language, folder, the flag, and sometimes an interface and comments — see `FORMAT.md` section 15). CPU certificates' keys are untouched for the same reason: no key in hand, nothing to render. Because any decryption would need the user's own password, pointing the reader at someone else's project still opens none of its protected blocks, so the "safe to point at someone else's project" property holds without a special rule. The "does not decrypt" statements in `AGENTS.md`, `architecture.md`, `GLOSSARY.md` and `FORMAT.md` describe this behaviour and are unchanged. Adding decryption later is a decision to reconstruct Siemens' key derivation with the legal question answered first; it is password-only by necessity, not by policy.
