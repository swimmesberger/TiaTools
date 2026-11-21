---
type: Architecture Decision Record
title: "A custom web control's preview runs in the platform's own web view, through Avalonia.Controls.WebView"
description: "A custom web control's preview runs in the platform's own web view, through Avalonia.Controls.WebView, only when a person asks for it."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: draft
---
# A custom web control's preview runs in the platform's own web view, through Avalonia.Controls.WebView

A custom web control of a Unified HMI device is a package of HTML, CSS and JavaScript (spec 0017), and the only way to see what it shows is to run it. The viewer shows its preview inside its own window in the platform's own web view (WebView2 on Windows, WKWebView on macOS, WebKitGTK on Linux), through `Avalonia.Controls.WebView`, Avalonia's own MIT-licensed control for Avalonia 12, which adds no other package. It runs only when a person asks for it, from a temporary folder that holds only that control's files, with no host objects and, where the platform allows, no network; where no web view is available, the viewer opens the same folder in the system browser instead. This stays a draft until the same check has passed on macOS.

## The Windows spike

A throwaway Avalonia 12.1.5 app with the viewer's Native AOT settings and `Avalonia.Controls.WebView` 12.1.2, on Windows, 2026-11-21:

- It builds and publishes with Native AOT without a single warning; the package talks to WebView2 through source-generated COM and needs no loader DLL. It adds about 1 MB to the executable.
- It runs: a local page loads and its script runs. It needs an application manifest listing Windows 10 and 11 as supported (`supportedOS` `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`); without one, Avalonia cannot create the native child window, with or without AOT. The viewer has no manifest today.
- WebView2 puts its profile next to the executable unless told otherwise; the viewer sets the data folder to a temporary one.
- The package can cancel a page's navigations and new windows, and report each request, but not block a request for an image or script: that takes WebView2's own interface through the control's platform handle, or browser arguments for the environment.
- It adds no host objects; the page can post messages to the host, which the viewer does not listen to. WebView2's empty `hostObjects` proxy is still there unless switched off through the same interface.
- `WebViewAdapterInfo.GetAdapterInfo` says whether WebView2 is installed; the viewer asks for WebView2 only, so it never falls back to the old EdgeHTML control.

## Considered options

- Open the control's start page in the system browser only: no dependency and no AOT risk, but the preview leaves the viewer, a browser opening a local file may refuse its scripts, and the browser's profile and network are the person's own. Kept as the fallback.
- `NativeWebView` (MIT, for Avalonia 12): the same platform web views and more render modes, but a single maintainer and little use so far.
- An embedded Chromium (CefGlue, CefSharp): one engine everywhere, but over 100 MB in every release, and its own security updates to follow.
- No preview: the files are shown as source, and seeing the control needs TIA or a device.

## Consequences

- The viewer can now run code it did not write, from an archive it was handed. That code runs only on request, isolated in its own folder, without runtime values and without the viewer's API, and the viewer says so before it starts.
- Windows needs the WebView2 runtime, which Windows 10 and 11 ship; Linux needs WebKitGTK, which not every system has; both fall back to the system browser.
- The release's notices gain the package's licence (`docs/releasing.md`).
- The viewer gains an application manifest on Windows.
- Blocking a control's network requests is extra interop with WebView2, WKWebView and WebKitGTK each; until it is written, the preview says that the control may reach the network.
- If the macOS check fails, this ADR is superseded before any viewer code depends on it, and the system browser becomes the preview there.
