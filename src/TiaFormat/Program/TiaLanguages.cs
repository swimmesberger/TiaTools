using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>A project language: its LCID, as core texts key their texts, and its culture name, such as de-DE.</summary>
public sealed record TiaLanguage(int Lcid, string Name);

/// <summary>A project language that is not one of the project's, named by the user.</summary>
public sealed class TiaLanguageException(string message) : Exception(message);

/// <summary>
/// A project's languages (spec 0014): the project languages in the project's order, the editing language and the
/// reference language. TIA keeps them as relations of the project, or of a global library, to its cultures.
/// </summary>
public sealed class TiaLanguages {
    private const string Culture = "Siemens.Automation.ObjectFrame.ICoreCulture";

    private TiaLanguages(IReadOnlyList<TiaLanguage> all, TiaLanguage? editing, TiaLanguage? reference) {
        All = all;
        Editing = editing;
        Reference = reference;
    }

    /// <summary>The project languages, in the order the project lists them; empty for a file that names none.</summary>
    public IReadOnlyList<TiaLanguage> All { get; }

    /// <summary>The language texts are typed in, and the one TIA's generated source is written in; null if none is named.</summary>
    public TiaLanguage? Editing { get; }

    /// <summary>The language TIA shows beside the editing language for translating; null if none is named.</summary>
    public TiaLanguage? Reference { get; }

    /// <summary>Reads the languages of the project, or of the library if the file holds no project.</summary>
    public static TiaLanguages Read(TiaDatabase db) {
        var schema = db.Schema;
        var used = schema.Relation("UsedCultures");
        var editing = schema.Relation("EditingCulture");
        var reference = schema.Relation("ReferenceCulture");
        var lcid = schema.Attribute(Culture, "LCID");
        var name = schema.Attribute(Culture, "Name");
        if (FindOwner(db) is not { } owner) {
            return new TiaLanguages([], null, null);
        }
        var all = new List<TiaLanguage>();
        TiaLanguage? editingLanguage = null, referenceLanguage = null;
        foreach (var relation in db.EnumerateRelations(owner)) {
            if (!db.TryGetFrame(relation.Target, out var culture) || db.ReadInteger(culture, lcid) is not { } id || db.ReadString(culture, name) is not { } cultureName) {
                continue;
            }
            var language = new TiaLanguage((int)id, cultureName);
            if (used.Matches(relation.RelationId)) {
                all.Add(language);
            } else if (editing.Matches(relation.RelationId)) {
                editingLanguage = language;
            } else if (reference.Matches(relation.RelationId)) {
                referenceLanguage = language;
            }
        }
        return new TiaLanguages(all, editingLanguage, referenceLanguage);
    }

    /// <summary>The project language of a culture name, ignoring case; throws, naming the project languages, if there is none.</summary>
    public TiaLanguage Find(string name) =>
        All.FirstOrDefault(language => string.Equals(language.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new TiaLanguageException(All.Count == 0
                ? $"{name} is not a project language: the file names no project languages."
                : $"{name} is not a project language. The project languages are {string.Join(", ", All.Select(Describe))}.");

    /// <summary>
    /// The order a text is read in when it is written in <paramref name="written"/>: that language, then the reference
    /// language, then the other project languages in their order (FR-004). Empty if the file names no languages.
    /// </summary>
    public IReadOnlyList<int> Preference(TiaLanguage? written) {
        var order = new List<int>();
        foreach (var language in new[] { written, Reference }.Concat(All)) {
            if (language is not null && !order.Contains(language.Lcid)) {
                order.Add(language.Lcid);
            }
        }
        return order;
    }

    /// <summary>A language's name, marked if it is the editing or the reference language.</summary>
    public string Describe(TiaLanguage language) {
        var marks = new[] { language == Editing ? "editing" : null, language == Reference ? "reference" : null }.OfType<string>().ToList();
        return marks.Count == 0 ? language.Name : $"{language.Name} ({string.Join(", ", marks)})";
    }

    private static TiaFrame? FindOwner(TiaDatabase db) {
        TiaFrame? library = null;
        foreach (ref readonly var frame in db.Frames) {
            var type = db.Schema.FindObjectType(frame.TypeId)?.FullName;
            if (type == "Siemens.Automation.DomainModel.ProjectData") {
                return frame;
            }
            if (library is null && type is not null && type.EndsWith("LibraryData", StringComparison.Ordinal)) {
                library = frame;
            }
        }
        return library;
    }
}
