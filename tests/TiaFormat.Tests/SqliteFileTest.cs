using AwesomeAssertions;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>ADR 0014: the SQLite reader of our own, on the cross-reference lists TIA wrote into the reference archives.</summary>
public sealed class SqliteFileTest {
    private static SqliteFile CrossReferenceList(string project) =>
        SqliteFile.Open(TiaDatabase.ReadProjectFile(ReferenceProjects.Archive(project), "XRef/XRef.db")
            ?? throw new InvalidOperationException($"{project} has no XRef/XRef.db."));

    private static int Count(SqliteFile file, string table) => file.Rows(file.FindTable(table)!).Count();

    // The counts are what Python's sqlite3 module gives for the same files.
    [Test]
    [Arguments("Closed", 5, 10, 14, 0)]
    [Arguments("FillingLine", 32, 100, 152, 18)]
    [Arguments("Languages", 56, 189, 310, 11)]
    [Arguments("Plcs", 19, 29, 41, 6)]
    [Arguments("Tables", 65, 204, 333, 14)]
    [Arguments("Text", 8, 14, 12, 3)]
    [Arguments("Types", 10, 31, 43, 0)]
    public void EveryRowOfTiasCrossReferenceListIsRead(string project, int parts, int objs, int rels, int addrs) {
        var file = CrossReferenceList(project);

        file.Tables.Select(table => table.Name).Should().BeEquivalentTo("info", "parts", "objs", "rels", "addrs");
        Count(file, "parts").Should().Be(parts);
        Count(file, "objs").Should().Be(objs);
        Count(file, "rels").Should().Be(rels);
        Count(file, "addrs").Should().Be(addrs);
    }

    [Test]
    public void AnIntegerPrimaryKeyColumnHoldsTheRowId() {
        var file = CrossReferenceList("Languages");
        var parts = file.FindTable("parts")!;

        var ids = file.Rows(parts).Select(row => row.Values[parts.Column("id")]).ToList();

        ids.Should().Equal(Enumerable.Range(1, 56).Select(id => (object?)(long)id));
    }

    [Test]
    public void RecordsGiveIntegersTextAndBlobs() {
        var file = CrossReferenceList("Languages");
        var info = file.FindTable("info")!;
        var parts = file.FindTable("parts")!;

        var row = file.Rows(info).Single();
        var part = file.Rows(parts).ElementAt(1);

        row.Integer(info.Column("version")).Should().Be(10);
        row.Values[info.Column("savepointID")].Should().BeOfType<string>().Which.Should().HaveLength(36);
        part.Blob(parts.Column("binary")).Should().HaveCount(14).And.StartWith("OG"u8.ToArray());
    }

    [Test]
    public void AFileThatIsNotSqliteIsRefused() {
        var act = () => SqliteFile.Open(new byte[4096]);

        act.Should().Throw<TiaFormatException>();
    }

    [Test]
    public void ColumnsComeFromTheCreateStatement() {
        var (columns, rowId) = SqliteFile.ParseColumns("""
            CREATE TABLE objs (
                id integer primary key,
                scopeObject integer references parts(id) on delete cascade not null,
                subObject binary not null,
                constraint objs_must_be_unique unique(scopeObject, subObject)
            )
            """);

        columns.Should().Equal("id", "scopeObject", "subObject");
        rowId.Should().Be(0);
    }
}
