using AwesomeAssertions;

namespace TiaViewer.Tests;

/// <summary>About shows the version, and the commit it was built from when the build knew it.</summary>
public sealed class AppInfoTest {
    [Test]
    public void The_version_shows_the_commit_shortened() {
        AppInfo.WithCommit("0.1.0+cba57be0123456789abcdef").Should().Be("0.1.0 (cba57be)");
    }

    [Test]
    public void A_version_without_a_commit_is_shown_as_it_is() {
        AppInfo.WithCommit("0.1.0").Should().Be("0.1.0");
    }

    [Test]
    public void The_short_version_drops_the_commit() {
        AppInfo.ShortVersion("0.1.0+cba57be").Should().Be("0.1.0");
    }
}
