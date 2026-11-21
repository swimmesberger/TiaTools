using System.Text;
using AwesomeAssertions;
using TiaFormat;
using TiaFormat.Program;

namespace TiaFormat.Tests;

public sealed class TiaSourceRenderingTest {
    private static string Scl(string xml, out int unknown) {
        using var output = new PooledByteWriter();
        unknown = SclRenderer.Render(Encoding.UTF8.GetBytes(xml), output);
        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    [Test]
    public void SclComesOutInDocumentOrderWithItsLayoutAndComments() {
        var text = Scl("""
            <SCLSource><Symbols><LocalTag SymID="3" RefId="1" /></Symbols><RootStatements><BC><Fold><BCL TE="head" /><BCE /></Fold></BC><NL UId="1" /><LC TE=" note" /><NL UId="2" /><Statement TE="IF" UId="3" SI="IF"><Fold UId="4"><BL /><Expression Result="Bool" UId="5"><SymVa UId="6" SyId="3" ODN="#a" /></Expression><BL /><KwTHEN UId="7" /><NL UId="8" /><BL NumBLs="4" /><Statements><Statement UId="9" SI="STAss"><Expression UId="10"><SymVa UId="11" SyId="7" ODN="&quot;DB&quot;" /><Dot UId="12" /><SymVa UId="13" SyId="8" ODN="x" /></Expression><BL /><OpAs UId="14" /><BL /><Const TE="1" UId="15" /><FiSt UId="16" /></Statement></Statements><NL UId="17" /><KwENDIF UId="18" /><FiSt UId="19" /></Fold></Statement></RootStatements></SCLSource>
            """, out var unknown);

        text.Should().Be("(*head*)\n// note\nIF #a THEN\n    \"DB\".x := 1;\nEND_IF;");
        unknown.Should().Be(0);
    }

    [Test]
    public void AnInstructionNameComesFromTheSymbolTable() {
        var text = Scl("""
            <SCLSource><Symbols><Instruction SymID="134" RefId="118" Name="BLKMOV" /></Symbols><RootStatements><FctCa UId="1"><Sub UId="2" SyId="134" /><BracO UId="3" /><BracC UId="4" /></FctCa></RootStatements></SCLSource>
            """, out _);

        text.Should().Be("BLKMOV()");
    }

    [Test]
    public void ATagWithoutARuleShowsInTheTextAndIsCounted() {
        var text = Scl("""<SCLSource><RootStatements><KwNew UId="1" /></RootStatements></SCLSource>""", out var unknown);

        text.Should().Be("«KwNew»");
        unknown.Should().Be(1);
    }

    [Test]
    public void ABoolFromTheDataBlockImageIsItsOwnBit() {
        byte[] image = [0x02];

        TiaProgram.FormatScalar("0x02000001", image, 0, 0).Should().Be("FALSE");
        TiaProgram.FormatScalar("0x02000001", image, 0, 1).Should().Be("TRUE");
    }

    [Test]
    public void AFailSafeNetworkIsWrittenInStatementFormFromItsStoredParts() {
        var idents = new TiaIdentTable();
        idents.Add(Encoding.UTF8.GetBytes("""
            <IdentXmlPart><InterfaceAccess><ID N="" S="Local" RID="1"><CS /></ID><SSD><AOS><AO N="in"><TD T="Bool:33554433:Bool" /></AO></AOS></SSD></InterfaceAccess><InterfaceAccess><ID N="" S="Local" RID="2" /><SSD><AOS><AO N="s"><TD /></AO><AO N="odd name"><TD /></AO></AOS></SSD></InterfaceAccess></IdentXmlPart>
            """));
        idents.Complete();
        var xml = """
            <FlgNet xmlns="x" Lang="F_FBD"><Parts><ORef UId="24" RefId="1" /><ORef UId="26" RefId="2" /><ORef UId="27" /><Part UId="21" Gate="A"><Negated PinName="in1" /></Part><Part UId="23" Gate="Coil" /></Parts><Wires><Wire UId="28"><OCon UId="24" /><PCon UId="21" PinName="in1" /></Wire><Wire UId="29"><PCon UId="21" PinName="out" /><PCon UId="23" PinName="in" /></Wire><Wire UId="30"><OCon UId="26" /><PCon UId="23" PinName="operand" /></Wire><Wire UId="31"><PCon UId="23" PinName="out" /><OCon UId="27" /></Wire></Wires></FlgNet>
            """;
        using var output = new PooledByteWriter();

        FlgNetRenderer.Render(Encoding.UTF8.GetBytes(xml), idents, output);

        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be("#s.\"odd name\" := NOT #in;");
    }

    [Test]
    public void AnStlCallListsItsParametersByNameWithTheirAssignmentsInOneColumn() {
        var idents = new TiaIdentTable();
        idents.Add(Encoding.UTF8.GetBytes("""
            <IdentXmlPart><InterfaceAccess><ID N="" S="Local" RID="1" /><SSD><AOS><AO N="count"><TD /></AO></AOS></SSD></InterfaceAccess><FCBlock><ID N="Limit" S="Global" RID="2" /></FCBlock><BlockInterfaceInfo><ID N="" S="BlockInterfaceInfo" RID="3" /><BIID><BPIL><BPI N="value" S="Input" /><BPI N="Ret_Val" S="Return" /></BPIL></BIID></BlockInterfaceInfo><Label><ID N="done" S="Local" RID="4" /></Label></IdentXmlPart>
            """));
        idents.Complete();
        var xml = """
            <Statements Version="14.0.0.0"><Statement UId="1"><Token Kw="1" DispName="A" /><OpdAccess RefId="1" UId="2" /><LC DispName=" counted" UId="3" /></Statement><Statement UId="4"><Token Kw="64" DispName="CALL" /><OpdAccess RefId="2" UId="5" /><CallInfo RefId="3" UId="6"><ParaExpression FPNum="2" UId="7"><OpdAccess RefId="1" UId="8" /></ParaExpression><ParaExpression FPNum="1" UId="9"><UserToken Type="Constant" DispName="7" /></ParaExpression></CallInfo></Statement><Statement UId="10"><Label><OpdAccess RefId="4" UId="11" /><Token Kw=":" /></Label><Token DispName="NOP" /><UserToken Type="Constant" DispName="0" /></Statement></Statements>
            """;
        using var output = new PooledByteWriter();

        StlRenderer.Render(Encoding.UTF8.GetBytes(xml), idents, output);

        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be(
            "      A #count ;// counted\n      CALL \"Limit\"\n      (  value                       := 7 , \n         Ret_Val                     := #count\n      );\ndone:      NOP 0;");
    }
}
