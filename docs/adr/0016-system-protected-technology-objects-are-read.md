---
type: Architecture Decision Record
title: "Technology objects under Siemens' system protection are shown as far as they are stored in clear"
description: "A technology object that TIA marks as protected by Siemens, such as a PID controller or a high-speed counter, stores its parameters in clear and TIA shows them without a password, so the reader shows them; only ciphertext stays closed."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T16:00:00Z }
status: stable
---
# Technology objects under Siemens' system protection are shown as far as they are stored in clear

TIA marks the technology objects of PID_Compact, PID_3Step, PID_Temp, High_Speed_Counter and SSI_Absolute_Encoder as know-how protected (`IsKnowHowProtected`, `Protection = SystemKnowHowProtection`): the protection is Siemens' own, on the instruction's code, and no user set it. Their parameters, the names of their members and the values set on them are stored in clear, and TIA's parameter view shows them to anyone who opens the project. The reader shows these objects' parameters like those of any other technology object (spec 0013), and keeps closed whatever is stored encrypted. This refines the rule of `AGENTS.md` that a protected block is reported as closed and nothing more is rendered: that rule follows from ADR 0011's reason, that a protected block holds only ciphertext, and these objects do not.

## Considered options

- **Show what is stored in clear** (chosen): it is reading the format and nothing more, as ADR 0011 says of any plaintext a protected block leaves; the configuration of a controller or a counter is the user's, not Siemens' code; and without it the reader would hide what TIA shows freely.
- **Report them as closed**, by the flag: the simple rule, but it would leave out every PID controller's ranges and tuning, which a reader of the program needs, for no protection the user chose.

## Consequences

The reader decides by what is stored, not by the flag alone: an encrypted part, under any protection, stays closed. `AGENTS.md`'s rule and `docs/FORMAT.md` section 15 say that a technology object under system protection is the exception, with this ADR as the reason. Siemens' system blocks themselves, such as the `PID_Compact` FB, stay closed: their code is ciphertext.
