---
type: Architecture Decision Record
title: "The text the tools write is for a coding agent, so LAD, FBD and GRAPH are written in statement form"
description: "The text the tools write is for a coding agent to understand, so LAD, FBD and GRAPH logic is written in statement form; the viewer may show it otherwise."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T14:00:00Z }
status: stable
---
# The text the tools write is for a coding agent, so LAD, FBD and GRAPH are written in statement form

The text TiaTools writes, `tiadump`'s output and the viewer's export and copy, is written first to be understood by a coding agent; the viewer is for people, and may show a block in another way, as long as what it exports is that text. TIA Portal has no text form for LAD, FBD or GRAPH, and the reader's first layout, one part call per line, is close to how TIA stores a network but makes the reader trace wires by part number. So their logic is written in statement form: assignments of AND, OR and NOT expressions where the parts form plain logic, calls for the rest, and GRAPH steps and transitions with their actions and conditions (spec 0009). The form is TiaTools' own, so it is checked by writing TIA's own export of each block in the same form.

## Considered options

- Keep the part-call layout: faithful to the stored graph, but neither an agent nor a person sees at a glance what a network does.
- Write TIA's SimaticML: TIA's own form, but long XML in which the logic is hard to find, for an agent as for a person.
- Draw rungs, boxes and sequences as TIA does: familiar to people who know TIA, and allowed in the viewer, but no text for an agent, so never the export.

## Consequences

- A dump's text for LAD and FBD blocks changes once, for every user, from part calls to statements.
- No TIA text can be compared with the form byte for byte; the check is that TIA's export, written by the same rules, gives the same text.
- What a network stores that has no expression, such as a box with an enable input, still needs a call form, so the text mixes both.
- A drawn view in the viewer needs its own spec, and its export still writes the statement form.
