using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TiaViewer;

/// <summary>
/// Tells macOS which files the viewer opened, so that they are among its recent documents in the Dock and the Apple menu.
/// Avalonia has no call for this, so it goes to <c>NSDocumentController</c> through the Objective-C runtime.
/// macOS keeps such a list only for an app bundle that declares the file types; run unbundled, these calls change nothing.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class MacRecentDocuments {
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    /// <summary>Adds a file or folder to the system's recent documents of this app. Must run on the UI thread.</summary>
    public static void Note(string path) {
        var text = SendText(Class("NSString"), Selector("stringWithUTF8String:"), path);
        var url = Send(Class("NSURL"), Selector("fileURLWithPath:"), text);
        if (url != 0) {
            Send(Controller(), Selector("noteNewRecentDocumentURL:"), url);
        }
    }

    /// <summary>Empties the system's recent documents of this app. Must run on the UI thread.</summary>
    public static void Clear() => Send(Controller(), Selector("clearRecentDocuments:"), 0);

    private static nint Controller() => Send(Class("NSDocumentController"), Selector("sharedDocumentController"));

    [LibraryImport(ObjectiveC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Class(string name);

    [LibraryImport(ObjectiveC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SendText(nint receiver, nint selector, string argument);
}
