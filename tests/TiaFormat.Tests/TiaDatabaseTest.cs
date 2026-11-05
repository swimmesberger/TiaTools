using System.Text;
using AwesomeAssertions;
using TiaFormat;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

public sealed class TiaDatabaseTest {
    private const uint ThingType = 0x00100001;
    private const uint OtherType = 0x00100002;

    private const string Schema = """
        <AttributeSet name="IThing" id="0x1" persistent="true">
          <Attribute name="Number" id="0" type="xs:int"/>
          <Attribute name="Name" id="1" type="xs:string"/>
          <Attribute name="Data" id="2" type="pe:BlobT"/>
          <Attribute name="Fixed" id="3" type="xs:int" constant="true"/>
          <Attribute name="Late" id="4" type="xs:int"/>
        </AttributeSet>
        <ObjectType name="Thing" id="0x00100001">
          <Implements ref="N.IThing"/>
          <Relation name="Owner" id="0x00110001" cardinality="1"><Target ref="N.Other"/></Relation>
          <Relation name="Few" id="0x00110002" cardinality="*"><Target ref="N.Other"/></Relation>
          <Relation name="Bravo" id="0x00110003" cardinality="*"><Target ref="N.Other"/></Relation>
          <Relation name="Alpha" id="0x00110004" cardinality="*"><Target ref="N.Other"/></Relation>
        </ObjectType>
        <ObjectType name="Other" id="0x00100002"/>
        """;

    private static byte[] ThingSegment(int number, string name, byte[]? data = null) {
        var values = new List<(int, byte[])> { (8, TiaFileBuilder.String(name)) };
        if (data is not null) {
            values.Add((12, data));
        }
        var segment = TiaFileBuilder.Segment(16, [.. values]);
        BitConverter.TryWriteBytes(segment.AsSpan(4), number);
        return segment;
    }

    private static TiaFrame Find(TiaDatabase database, uint type, ulong id) {
        database.TryGetFrame(new TiaObjectKey(type, id), out var frame).Should().BeTrue();
        return frame;
    }

    [Test]
    public void LastFrameOfAnIdWins_AndADeletedLastFrameEndsTheObject() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(ThingType, 1, [ThingSegment(1, "old")]);
        file.WriteObject(ThingType, 2, [ThingSegment(2, "gone")]);
        file.WriteObject(ThingType, 1, [ThingSegment(3, "new")]);
        file.WriteObject(ThingType, 2, [null], states: 0x04);
        file.Commit();

        var database = TiaDatabase.Parse(file.ToArray());

        var number = database.Schema.Attribute("N.IThing", "Number");
        database.ReadInteger(Find(database, ThingType, 1), number).Should().Be(3);
        database.TryGetFrame(new TiaObjectKey(ThingType, 2), out _).Should().BeFalse();
    }

    [Test]
    public void FramesAfterTheLastCommitAreNotCurrent() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(ThingType, 1, [ThingSegment(1, "committed")]);
        file.Commit();
        file.WriteObject(ThingType, 1, [ThingSegment(2, "torn write")]);

        var database = TiaDatabase.Parse(file.ToArray());

        database.ReadString(Find(database, ThingType, 1), database.Schema.Attribute("N.IThing", "Name")).Should().Be("committed");
    }

    [Test]
    public void AWrongFrameHashIsRejectedOnlyWhenAskedFor() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(ThingType, 1, [ThingSegment(1, "x")]);
        file.Commit();
        var bytes = file.ToArray();
        bytes[^1] ^= 0xFF;

        TiaDatabase.Parse(bytes).Should().NotBeNull();
        FluentActions.Invoking(() => TiaDatabase.Parse(bytes, verifyHashes: true)).Should().Throw<TiaFormatException>();
    }

    [Test]
    public void AnAttributePastAShortSegmentIsAbsent() {
        var file = new TiaFileBuilder(Schema);
        var shortSegment = TiaFileBuilder.Segment(4);
        BitConverter.TryWriteBytes(shortSegment.AsSpan(4), 7);
        file.WriteObject(ThingType, 1, [shortSegment]);
        file.Commit();

        var database = TiaDatabase.Parse(file.ToArray());
        var frame = Find(database, ThingType, 1);

        database.ReadInteger(frame, database.Schema.Attribute("N.IThing", "Number")).Should().Be(7);
        database.ReadInteger(frame, database.Schema.Attribute("N.IThing", "Late")).Should().BeNull("the segment ends before this slot");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void AnInlineBlobDecodesAcrossPages(bool compressed) {
        var raw = Encoding.ASCII.GetBytes(new string('a', 5000) + new string('b', 100));
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(ThingType, 1, [ThingSegment(1, "x", TiaFileBuilder.Blob(raw, compressed))]);
        file.Commit();

        var database = TiaDatabase.Parse(file.ToArray());
        using var blob = database.ReadBlob(Find(database, ThingType, 1), database.Schema.Attribute("N.IThing", "Data"));

        blob.Span.ToArray().Should().Equal(raw);
    }

    [Test]
    public void ABlobWithFewerPagesThanItsLengthIsRefused() {
        // Exposing the advertised length would show whatever an earlier renter left in the pooled array.
        var blob = TiaFileBuilder.Blob(new byte[4096], compress: false);
        var headAt = blob.Length - 4096 - TiaFileBuilder.VarInt(1).Length - 4 - 18 - 1;
        BitConverter.TryWriteBytes(blob.AsSpan(headAt + 1), 5000L);
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(ThingType, 1, [ThingSegment(1, "x", blob)]);
        file.Commit();

        var database = TiaDatabase.Parse(file.ToArray());
        var frame = Find(database, ThingType, 1);

        FluentActions.Invoking(() => {
            using var value = database.ReadBlob(frame, database.Schema.Attribute("N.IThing", "Data"));
        }).Should().Throw<TiaFormatException>();
    }

    [Test]
    public void AnUnsigned64BitValueIsReadWholeOrRefused_NeverReinterpreted() {
        var file = new TiaFileBuilder("""
            <AttributeSet name="IBig" id="0x1" persistent="true"><Attribute name="Huge" id="0" type="xs:ulong"/></AttributeSet>
            <ObjectType name="Big" id="0x00100003"><Implements ref="N.IBig"/></ObjectType>
            """);
        var segment = TiaFileBuilder.Segment(8);
        BitConverter.TryWriteBytes(segment.AsSpan(4), ulong.MaxValue);
        file.WriteObject(0x00100003, 1, [segment]);
        file.Commit();

        var database = TiaDatabase.Parse(file.ToArray());
        var frame = Find(database, 0x00100003, 1);
        var huge = database.Schema.Attribute("N.IBig", "Huge");

        database.ReadUnsignedInteger(frame, huge).Should().Be(ulong.MaxValue);
        FluentActions.Invoking(() => database.ReadInteger(frame, huge)).Should().Throw<TiaFormatException>();
    }

    [Test]
    public void ARelativeRelationTargetGivesTheTargetItsInverseRelation() {
        var file = new TiaFileBuilder("""
            <ObjectType name="Source" id="0x00100004">
              <Relation name="ToTarget" id="0x00110010" cardinality="1"><Target ref="Target"/><Inverse name="Back" id="0x00110011" cardinality="*"/></Relation>
            </ObjectType>
            <ObjectType name="Target" id="0x00100005"/>
            """);
        file.Commit();

        var database = TiaDatabase.Parse(file.ToArray());

        database.Schema.FindObjectType("N.Target")!.ToManyRelations.Should().ContainSingle().Which.Id.Should().Be(0x00110011);
    }

    [Test]
    public void ADumpPathIsNeverHandedOutTwice() {
        var paths = new TiaDumpPaths();

        // The suffixed form of a later name can already be some other block's own name.
        var handedOut = new[] {
            paths.Reserve("foo_.42.scl", 1),
            paths.Reserve("foo_.scl", 2),
            paths.Reserve("foo_.scl", 42),
            paths.Reserve("FOO_.scl", 42),
        };

        handedOut.Should().OnlyHaveUniqueItems(path => path.ToUpperInvariant());
    }

    [Test]
    public void ALargeBlobIsTheConcatenationOfItsSegments() {
        // Kind 4 is a head and references, not a varint length and bytes: reading it as such loses everything above 128 KB.
        var first = new byte[32 * 4096];
        Array.Fill(first, (byte)'x');
        var second = Encoding.ASCII.GetBytes("the end");
        var file = new TiaFileBuilder(Schema);
        file.WriteSystem(0x70013, 11, TiaFileBuilder.Head(first, compress: true));
        file.WriteSystem(0x70013, 12, TiaFileBuilder.Head(second, compress: true));
        file.WriteObject(ThingType, 1, [ThingSegment(1, "x", TiaFileBuilder.LargeBlob(first.Length + second.Length, 33, 11, 12))]);
        file.Commit();

        var database = TiaDatabase.Parse(file.ToArray());
        using var blob = database.ReadBlob(Find(database, ThingType, 1), database.Schema.Attribute("N.IThing", "Data"));

        blob.Length.Should().Be(first.Length + second.Length);
        Encoding.ASCII.GetString(blob.Span[^7..]).Should().Be("the end");
    }

    [Test]
    public void RelationsComeFromAllThreeListFormsInOrder() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(OtherType, 5, [null, null], info: 0);
        file.WriteObject(OtherType, 6, [null, null], info: 0);
        file.WriteObject(ThingType, 1, [
            ThingSegment(1, "x"),
            TiaFileBuilder.Slots((0x00110001, OtherType, 5)),
            TiaFileBuilder.Slots((0x00110002, 0, 0), (0x00110002, OtherType, 6), (0, 0, 0)),
            TiaFileBuilder.Targets((OtherType, 5)),
            TiaFileBuilder.Targets((OtherType, 6), (0, 0)),
        ], info: 1);
        file.Commit();

        var database = TiaDatabase.Parse(file.ToArray());
        var relations = new List<TiaRelation>();
        foreach (var relation in database.EnumerateRelations(Find(database, ThingType, 1))) {
            relations.Add(relation);
        }

        // The per-relation lists follow the schema's to-many relations sorted by name: Alpha, then Bravo.
        relations.Should().Equal(
            new TiaRelation(0x00110001, new TiaObjectKey(OtherType, 5)),
            new TiaRelation(0x00110002, default),
            new TiaRelation(0x00110002, new TiaObjectKey(OtherType, 6)),
            new TiaRelation(0x00110004, new TiaObjectKey(OtherType, 5)),
            new TiaRelation(0x00110003, new TiaObjectKey(OtherType, 6)),
            new TiaRelation(0x00110003, default));
        database.TryGetTarget(Find(database, ThingType, 1), database.Schema.Relation("Owner"), out var owner).Should().BeTrue();
        owner.ObjectId.Should().Be(5);
    }
}
