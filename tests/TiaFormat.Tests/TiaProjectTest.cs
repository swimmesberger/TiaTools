using System.Text;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>What a viewer stands on: tokens with their positions, references between blocks, the tree and search.</summary>
public sealed class TiaProjectTest {
    private const uint CodeBlock = 0x00221001;
    private const uint Root = 0x00221005;
    private const uint Unit = 0x00221006;
    private const uint DataBlock = 0x00221002;
    private const uint Current = 0x00222001;
    private const uint DbInterface = 0x00222003;
    private const uint GeneratedFromFb = 0x00222007;

    private static string Scl(string body) => "<SCLSource><Symbols /><RootStatements>" + body + "</RootStatements></SCLSource>";

    private static List<TiaToken> TokensOf(string body, out string text) {
        using var output = new PooledByteWriter();
        var tokens = new List<TiaToken>();
        SclRenderer.Render(Encoding.UTF8.GetBytes(Scl(body)), output, tokens);
        text = Encoding.UTF8.GetString(output.WrittenSpan);
        return tokens;
    }

    private static string TextOf(TiaToken token, string text) => text.Substring(token.Start, token.Length);

    [Test]
    public void StoredSourceSaysWhatEachTokenIs() {
        var tokens = TokensOf("""<Statement TE="IF" UId="1" SI="IF"><LC TE=" note" /><SymVa UId="2" SI="Var" ODN="#a" /><KwTHEN UId="3" /><Const TE="5" UId="4" /></Statement>""", out var text);

        tokens.Select(token => (TextOf(token, text), token.Kind)).Should().Equal(
            ("IF", TiaTokenKind.Keyword), ("// note", TiaTokenKind.Comment), ("#a", TiaTokenKind.Local), ("THEN", TiaTokenKind.Keyword), ("5", TiaTokenKind.Literal));
    }

    [Test]
    public void TheStatementFormOfANetworkOrSequenceShowsItsKeywords() {
        var document = TiaDocumentBuilder.FromText("IF #start AND NOT \"Fault\" THEN GOTO skip; END_IF;\nTRANSITION 1 Go: Idle -> Fill WHEN #full OR FALSE");

        document.Tokens.Where(token => token.Kind == TiaTokenKind.Keyword).Select(token => TextOf(token, document.Text)).Should()
            .Equal("IF", "AND", "NOT", "THEN", "GOTO", "END_IF", "TRANSITION", "WHEN", "OR", "FALSE");
    }

    [Test]
    public void ACalledFunctionIsACallToItsName() {
        var tokens = TokensOf("""<FctCa UId="1"><Sub UId="2" SI="FC" SyId="7" ODN="&quot;Other&quot;" /><BracO UId="3" /><BracC UId="4" /></FctCa>""", out _);

        tokens.Should().ContainSingle().Which.Should().Be(new TiaToken(0, 7, TiaTokenKind.Call, "Other", IsCall: true));
    }

    [Test]
    public void AnInstanceDataBlockInACallStatementIsWhatIsCalled() {
        var tokens = TokensOf("""<InstCa UId="1"><SymVa UId="2" SI="VarDB" ODN="&quot;Inst&quot;" /><BracO UId="3" /><SymVa UId="4" SI="Var" ODN="#x" /><BracC UId="5" /></InstCa>""", out _);

        tokens[0].Should().Be(new TiaToken(0, 6, TiaTokenKind.Global, "Inst", IsCall: true));
        tokens[1].IsCall.Should().BeFalse();
    }

    [Test]
    public void AMultiInstanceCallIsACallOfALocal() {
        var tokens = TokensOf("""<InstCa UId="1"><Sub UId="2" SI="FB" ODN="#inner" /><BracO UId="3" /><BracC UId="4" /></InstCa>""", out _);

        tokens.Should().ContainSingle().Which.Should().Be(new TiaToken(0, 6, TiaTokenKind.Local, null, IsCall: true));
    }

    [Test]
    public void AQuotedPartAfterADotLeadsNowhere() {
        var tokens = TokensOf("""<SymVa UId="1" SI="VarDB" ODN="&quot;Db&quot;" /><Dot UId="2" /><SymVa UId="3" SI="Var" ODN="&quot;Int&quot;" />""", out _);

        tokens[0].Target.Should().Be("Db");
        tokens[1].Should().Be(new TiaToken(5, 5, TiaTokenKind.Member));
    }

    [Test]
    public void TheScannerKnowsDeclarations() {
        var document = TiaDocumentBuilder.FromText("   VAR \n      a : \"Udt\" := 16#0F;   // says \"Other\"\n      \"1st\" : Int;\nEND_VAR");
        string Text(TiaToken token) => document.Text.Substring(token.Start, token.Length);

        document.Tokens.Select(token => (Text(token), token.Kind, token.Target)).Should().Equal(
            ("VAR", TiaTokenKind.Keyword, null),
            ("\"Udt\"", TiaTokenKind.Global, "Udt"),
            ("16#0F", TiaTokenKind.Literal, null),
            ("// says \"Other\"", TiaTokenKind.Comment, null),
            ("\"1st\"", TiaTokenKind.Member, null),
            ("Int", TiaTokenKind.Type, null),
            ("END_VAR", TiaTokenKind.Keyword, null));
    }

    [Test]
    public void AQuotedMemberBeingSetLeadsNowhereButAnOwnLineTypeDoes() {
        var document = TiaDocumentBuilder.FromText("\"Type\"\nBEGIN\n   \"1st\".x := 1;\n");

        document.Tokens.Where(token => token.Target is not null).Should().ContainSingle().Which.Target.Should().Be("Type");
    }

    [Test]
    public void ANameWithAQuoteInItDoesNotRunAcrossLines() {
        var document = TiaDocumentBuilder.FromText("\"odd\n\"Next\" : Int;\n");

        document.Tokens[0].Should().Be(new TiaToken(0, 4, TiaTokenKind.Member));
    }

    [Test]
    public void ADocumentFindsLinesTokensAndDeclarations() {
        var document = TiaDocumentBuilder.FromText("FUNCTION \"F\"\n   VAR \n      value : Int;\n   END_VAR\nBEGIN\n\tvalue := 1;\n");

        document.LineCount.Should().Be(7);
        document.LineText(2).Should().Be("      value : Int;");
        document.LineOf(document.LineStart(2) + 3).Should().Be(2);
        document.TokenAt(1)!.Value.Kind.Should().Be(TiaTokenKind.Keyword);
        document.TokenAt(8).Should().BeNull();
        document.DeclarationLine("#value").Should().Be(2);
        document.DeclarationLine("#missing").Should().BeNull();
    }

    internal static TiaFileBuilder File() => new(TiaSourceFileTest.Schema);

    internal static void Block(TiaFileBuilder file, ulong id, string name, string kind, string body, string members = "<Root />", ulong? generatedFrom = null) {
        file.WriteObject(Root, id + 100, [TiaSourceFileTest.Part(members, "BlockSource")]);
        file.WriteObject(Unit, id + 200, [TiaFileBuilder.Segment(8, (4, TiaFileBuilder.Blob(Encoding.UTF8.GetBytes(Scl(body)), compress: false)), (8, TiaFileBuilder.String("SCL")))]);
        file.WriteObject(CodeBlock, id, [
            TiaSourceFileTest.Core(name, kind),
            TiaSourceFileTest.General(kind, "SCL"),
            TiaSourceFileTest.NoHeader(),
            TiaFileBuilder.Slots((Current, Root, id + 100)),
            TiaFileBuilder.Targets((Unit, id + 200)),
        ]);
    }

    /// <summary>Written to a folder, as an archive would be: a project opens by path.</summary>
    internal static TiaProject Opened(TiaFileBuilder file) {
        file.Commit();
        var folder = Directory.CreateTempSubdirectory("tiaproject").FullName;
        Directory.CreateDirectory(Path.Combine(folder, "System"));
        System.IO.File.WriteAllBytes(Path.Combine(folder, "System", "PEData.plf"), file.ToArray());
        System.IO.File.WriteAllText(Path.Combine(folder, "Project.ap21"), "<Project />");
        try {
            var project = TiaProject.Open(Path.Combine(folder, "Project.ap21"));
            project.BuildIndex();
            return project;
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    private const string CallOfOther = """<FctCa UId="1"><Sub UId="2" SI="FC" ODN="&quot;Other&quot;" /><BracO UId="3" /><BracC UId="4" /></FctCa><FiSt UId="5" />""";

    [Test]
    public void AProjectOpensByTheFileItsFolderIsNamedAfter() {
        var file = File();
        Block(file, 1, "Other", "FC", "");

        Opened(file).Items.Should().ContainSingle().Which.Name.Should().Be("Other");
    }

    [Test]
    public void ACallIsFoundFromBothEnds() {
        var file = File();
        Block(file, 1, "Other", "FC", """<LC TE=" nothing" />""");
        Block(file, 2, "Caller", "FC", CallOfOther);

        var project = Opened(file);
        var other = project.Items.Single(item => item.Name == "Other");
        var caller = project.Items.Single(item => item.Name == "Caller");

        var reference = project.ReferencesTo(other).Should().ContainSingle().Subject;
        reference.From.Should().Be(caller);
        reference.Kind.Should().Be(TiaReferenceKind.Call);
        reference.Text.Should().Be("\"Other\"();");
        project.Document(caller).LineText(reference.Line).Should().Be("\t\"Other\"();");
        project.ReferencesFrom(caller).Should().ContainSingle().Which.To.Should().Be(other);
        project.ReferencesTo(caller).Should().BeEmpty();
    }

    [Test]
    public void ATokenPositionFitsTheWholeBlocksTextEvenAfterOtherAlphabets() {
        var file = File();
        Block(file, 1, "Other", "FC", "");
        Block(file, 2, "Caller", "FC", """<LC TE=" Größe €" /><NL UId="1" />""" + CallOfOther);

        var project = Opened(file);
        var document = project.Document(project.Items.Single(item => item.Name == "Caller"));
        var call = document.Tokens.Single(token => token.Target == "Other");

        document.Text.Substring(call.Start, call.Length).Should().Be("\"Other\"");
        document.Tokens.Should().BeInAscendingOrder(token => token.Start);
    }

    [Test]
    public void ACallThroughAMemberIsACallOfItsType() {
        var file = File();
        Block(file, 1, "Inner", "FB", "");
        Block(file, 2, "Outer", "FB", """<InstCa UId="1"><Sub UId="2" SI="FB" ODN="#inner" /><BracO UId="3" /><BracC UId="4" /></InstCa>""",
            """<Root><Member Name="Static"><Member ID="1" Name="inner" RID="0x93000001" Type="&quot;Inner&quot;" /></Member></Root>""");

        var project = Opened(file);
        var inner = project.Items.Single(item => item.Name == "Inner");

        project.ReferencesTo(inner).Select(reference => reference.Kind).Should().BeEquivalentTo([TiaReferenceKind.Use, TiaReferenceKind.Call]);
    }

    [Test]
    public void ACallThroughAnInstanceDataBlockIsACallOfItsFunctionBlockToo() {
        var file = File();
        Block(file, 1, "Fb", "FB", "");
        file.WriteObject(Root, 50, [TiaSourceFileTest.Part("<Values />", "Values")]);
        file.WriteObject(DataBlock, 51, [TiaSourceFileTest.Core("Fb.IDB", "DB"), TiaSourceFileTest.General("DB", "DB"), TiaSourceFileTest.NoHeader(), TiaFileBuilder.Slots((DbInterface, Root, 50), (GeneratedFromFb, CodeBlock, 1))]);
        Block(file, 2, "Caller", "FC", """<InstCa UId="1"><SymVa UId="2" SI="VarDB" ODN="&quot;Fb.IDB&quot;" /><BracO UId="3" /><BracC UId="4" /></InstCa>""");

        var project = Opened(file);
        var calls = project.ReferencesFrom(project.Items.Single(item => item.Name == "Caller"));

        calls.Should().OnlyContain(reference => reference.Kind == TiaReferenceKind.Call);
        calls.Select(reference => reference.To.Name).Should().BeEquivalentTo(["Fb.IDB", "Fb"]);
    }

    [Test]
    public void TextInACommentIsNoReference() {
        var file = File();
        Block(file, 1, "Other", "FC", "");
        Block(file, 2, "Caller", "FC", """<LC TE=" see &quot;Other&quot;" />""");

        var project = Opened(file);

        project.ReferencesTo(project.Items.Single(item => item.Name == "Other")).Should().BeEmpty();
    }

    [Test]
    public void AClosedBlockIsCountedSoAnEmptyListCanSaySo() {
        var file = File();
        Block(file, 1, "Open", "FC", "");
        file.WriteObject(Root, 60, [TiaSourceFileTest.Part("<Root />", "BlockSource")]);
        var core = TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String("Closed")), (8, TiaFileBuilder.String("FC")));
        core[12] = 1;
        file.WriteObject(CodeBlock, 61, [core, TiaSourceFileTest.General("FC", "SCL"), TiaSourceFileTest.NoHeader(), TiaFileBuilder.Slots((Current, Root, 60))]);

        var project = Opened(file);
        var closed = project.Items.Single(item => item.Name == "Closed");

        closed.IsClosed.Should().BeTrue();
        project.ClosedBlocksAround(project.Items.Single(item => item.Name == "Open")).Should().Be(1);
        project.Document(closed).Text.Should().Contain("know-how protected");
    }

    [Test]
    public void SearchFindsEachLineOnceWhateverItsCase() {
        var file = File();
        Block(file, 1, "One", "FC", """<LC TE=" Pump pump" /><NL UId="1" /><LC TE=" valve" />""");

        var results = Opened(file).Search("PUMP");

        results.Should().ContainSingle().Which.Text.Should().Be("// Pump pump");
    }

    [Test]
    public void SearchStopsAtItsLimit() {
        var file = File();
        Block(file, 1, "One", "FC", """<LC TE=" pump" /><NL UId="1" /><LC TE=" pump" /><NL UId="2" /><LC TE=" pump" />""");

        Opened(file).Search("pump", limit: 2).Should().HaveCount(2);
    }

    [Test]
    public void TheTreeSortsFoldersBeforeItemsAndNames() {
        var file = File();
        Block(file, 1, "b", "FC", "");
        Block(file, 2, "A", "FC", "");

        var project = Opened(file);
        var holder = project.Root.Children.Single().Children.Single();

        holder.Children.Select(node => node.Name).Should().Equal("A", "b");
    }
}
