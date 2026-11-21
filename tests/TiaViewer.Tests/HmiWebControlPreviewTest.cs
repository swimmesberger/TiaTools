using System.Text;
using AwesomeAssertions;
using TiaFormat.Program;

namespace TiaViewer.Tests;

/// <summary>Spec 0017, FR-010: what a custom web control's preview writes before its start page opens.</summary>
public sealed class HmiWebControlPreviewTest {
    private static TiaHmiPackageFile File(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    private static TiaHmiCustomWebControl Control(params TiaHmiPackageFile[] files) => new("{0}", "Gauge", [("Main", "Gauge_1")], "CustomControls/{0}.zip", files);

    [Test]
    public void The_preview_writes_only_the_controls_files_into_a_new_folder_and_opens_its_start_page() {
        var control = Control(
            File("manifest.json", """{ "control": { "identity": { "start": "./page/main.html" } } }"""),
            File("page/main.html", "<p>gauge</p>"));

        var start = HmiWebControlPreview.Unpack(control);
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(start))!;

        start.Should().EndWith(Path.Combine("page", "main.html"));
        System.IO.File.ReadAllText(start).Should().Be("<p>gauge</p>");
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Should().HaveCount(2);
        Directory.Delete(folder, recursive: true);
    }

    [Test]
    public void Without_a_start_page_in_its_manifest_a_control_starts_at_control_index_html() {
        HmiWebControlPreview.StartPage(Control(File("manifest.json", "{}"))).Should().Be("control/index.html");
    }

    [Test]
    public void A_package_that_names_a_file_outside_its_folder_is_refused() {
        var act = () => HmiWebControlPreview.Unpack(Control(File("../escape.txt", "x")));

        act.Should().Throw<InvalidDataException>();
    }
}
