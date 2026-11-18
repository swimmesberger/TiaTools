---
type: Architecture Decision Record
title: "The README's pictures are rendered headless by a tool in the solution"
description: "The README's pictures are rendered headless by a tool in the solution, on Avalonia.Headless, so they can be taken again with one command."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T15:00:00Z }
status: draft
---
# The README's pictures are rendered headless by a tool in the solution

The README shows the viewer in six pictures, which go stale whenever the viewer's look changes. `tools/ReadmePictures` opens the demo project in the real `MainWindow` on Avalonia's headless platform, puts it into each scene and saves the rendered frame, so the same command gives the same pixels on the same machine. It adds `Avalonia.Headless`, at the version of the other Avalonia packages, and it is in `TiaTools.slnx` so that the build catches it when the viewer's names change.

## Considered options

- Screenshots of a real window, by hand or by a script: they bring the system's window frame and scaling, differ between machines, and a hand-taken one cannot be repeated exactly.
- A script kept outside the repository: the first one was lost with the session that wrote it, which is why this ADR exists.

## Consequences

- Text is drawn with the fonts of the machine that runs it, so pictures taken on a Mac look different from the Windows ones; take them on Windows, as the current ones are.
- The headless platform has no native menu, so the menu bar is drawn in the window, as on Windows.
- `MainWindow` takes its recent files as a constructor argument, so the tool does not add the demo to the user's list.
