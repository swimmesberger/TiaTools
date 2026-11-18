using AwesomeAssertions;

namespace TiaViewer.Tests;

/// <summary>The viewer remembers what it opened, most recent first, across runs.</summary>
public sealed class RecentFilesTest : IDisposable {
    private readonly string _folder = Directory.CreateTempSubdirectory("tiaviewer-recent-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string ListFile => Path.Combine(_folder, "settings", "recent.txt");

    /// <summary>A file that exists, in a folder of its own so that names can repeat.</summary>
    private string Archive(string name, string folder = "projects") {
        var path = Path.Combine(_folder, folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Test]
    public void A_file_that_was_opened_comes_first() {
        var recent = new RecentFiles(ListFile);
        var first = Archive("First.zap20");
        var second = Archive("Second.zap21");

        recent.Add(first);
        recent.Add(second);

        recent.Paths.Should().Equal(second, first);
    }

    [Test]
    public void Opening_a_file_again_moves_it_to_the_top_without_a_second_entry() {
        var recent = new RecentFiles(ListFile);
        var first = Archive("First.zap20");
        var second = Archive("Second.zap21");
        recent.Add(first);
        recent.Add(second);

        recent.Add(first);

        recent.Paths.Should().Equal(first, second);
    }

    [Test]
    public void The_list_keeps_the_ten_most_recent() {
        var recent = new RecentFiles(ListFile);
        var paths = Enumerable.Range(1, 12).Select(number => Archive($"Project{number}.ap21")).ToList();

        foreach (var path in paths) {
            recent.Add(path);
        }

        recent.Paths.Should().HaveCount(RecentFiles.Capacity).And.Equal(Enumerable.Reverse(paths).Take(RecentFiles.Capacity));
    }

    [Test]
    public void A_file_that_no_longer_exists_is_left_out() {
        var recent = new RecentFiles(ListFile);
        var kept = Archive("Kept.zap18");
        var deleted = Archive("Deleted.zap18");
        recent.Add(kept);
        recent.Add(deleted);

        File.Delete(deleted);

        recent.Paths.Should().Equal(kept);
    }

    [Test]
    public void The_list_is_there_again_after_a_restart() {
        var first = Archive("First.al21");
        var second = Archive("Second.zap15_1");
        var before = new RecentFiles(ListFile);
        before.Add(first);
        before.Add(second);

        var after = new RecentFiles(ListFile);

        after.Paths.Should().Equal(second, first);
    }

    [Test]
    public void Two_viewers_open_at_once_keep_each_others_files() {
        var one = new RecentFiles(ListFile);
        var other = new RecentFiles(ListFile);
        var first = Archive("First.zap20");
        var second = Archive("Second.zap20", "elsewhere");
        var third = Archive("Third.zap20");

        one.Add(first);
        other.Add(second);
        one.Add(third);

        one.Paths.Should().Equal(third, second, first);
    }

    [Test]
    public void Clear_forgets_every_file_also_after_a_restart() {
        var recent = new RecentFiles(ListFile);
        recent.Add(Archive("First.zap20"));
        recent.Add(Archive("Second.zap21"));

        recent.Clear();

        recent.Paths.Should().BeEmpty();
        new RecentFiles(ListFile).Paths.Should().BeEmpty();
    }

    [Test]
    public void Remove_forgets_one_file() {
        var recent = new RecentFiles(ListFile);
        var kept = Archive("Kept.zap20");
        var removed = Archive("Removed.zap20");
        recent.Add(kept);
        recent.Add(removed);

        recent.Remove(removed);

        recent.Paths.Should().Equal(kept);
    }

    [Test]
    public void A_change_is_announced() {
        var recent = new RecentFiles(ListFile);
        var changes = 0;
        recent.Changed += (_, _) => changes++;

        recent.Add(Archive("First.zap20"));
        recent.Clear();

        changes.Should().Be(2);
    }

    [Test]
    public void Without_a_list_file_the_list_is_empty() {
        new RecentFiles(ListFile).Paths.Should().BeEmpty();
    }

    [Test]
    public void A_list_that_cannot_be_written_still_serves_this_run() {
        // A file where the list's folder should be: the folder cannot be created.
        File.WriteAllText(Path.Combine(_folder, "settings"), "");
        var recent = new RecentFiles(ListFile);
        var path = Archive("First.zap20");

        recent.Add(path);

        recent.Paths.Should().Equal(path);
    }

    [Test]
    public void A_label_is_the_file_name() {
        RecentFiles.Labels([Path.Combine(_folder, "a", "Line.zap21"), Path.Combine(_folder, "b", "Press.ap20")])
            .Should().Equal("Line.zap21", "Press.ap20");
    }

    [Test]
    public void Files_of_the_same_name_are_told_apart_by_their_folder() {
        RecentFiles.Labels([Path.Combine(_folder, "Site A", "Line.zap21"), Path.Combine(_folder, "Site B", "Line.zap21"), Path.Combine(_folder, "c", "Press.ap20")])
            .Should().Equal("Line.zap21 — Site A", "Line.zap21 — Site B", "Press.ap20");
    }

    [Test]
    public void A_folder_is_labelled_by_its_name() {
        RecentFiles.Labels([Path.Combine(_folder, "Unpacked") + Path.DirectorySeparatorChar]).Should().Equal("Unpacked");
    }
}
