using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>Spec 0004: the OPC UA report, on a small invented project with two PLCs.</summary>
public sealed class TiaOpcUaTest {
    private const uint DeviceItem = TiaOpcUaFixture.DeviceItem;
    private const uint ProjectUser = TiaOpcUaFixture.ProjectUser;
    private static readonly DateTime Compiled = TiaOpcUaFixture.Compiled;

    // Helpers --------------------------------------------------------------------------------------------------------

    private static TiaOpcUaReport Report(TiaOpcUaFixture.Options? options = null) => TiaOpcUa.Read(new TiaProgram(TiaOpcUaFixture.NewProject(options)));

    private static TiaOpcUaPlc Filler(TiaOpcUaFixture.Options? options = null) => Report(options).Plcs.Single(plc => plc.Name == "Filler");

    private static string Markdown(TiaOpcUaReport report) {
        var output = new StringWriter();
        TiaOpcUaWriter.WriteMarkdown(report, "Filling line", output);
        return output.ToString().ReplaceLineEndings("\n");
    }

    private static string Inspected(TiaDatabase database, uint type, ulong id) {
        database.TryGetFrame(new TiaObjectKey(type, id), out var frame).Should().BeTrue();
        var output = new StringWriter();
        TiaInspector.Write(database, frame, output);
        return output.ToString().ReplaceLineEndings("\n");
    }

    // Is the OPC UA server on, and how is it set up ------------------------------------------------------------------

    [Test]
    public void AnEnabledServerIsReportedOnWithItsSettings() {
        var server = Filler().Server!;

        server.IsEnabled.Should().BeTrue();
        server.ApplicationName.Should().Be("Filler.OPC-UA");
        server.SecurityPolicies.Should().Contain(new TiaOpcUaSecurityPolicy("None", false))
            .And.Contain(new TiaOpcUaSecurityPolicy("256SHA_SE", true))
            .And.Contain(new TiaOpcUaSecurityPolicy("256_S", null));
        server.AutoAcceptClientCertificates.Should().BeFalse();
        server.MinimumPublishingInterval.Should().Be(250);
        Markdown(Report()).Should().Contain("**Server: on.**").And.Contain("| Minimum publishing interval | 250 ms |").And.Contain("`256_S` not stored");
    }

    [Test]
    public void PortUserAuthenticationStandardInterfaceAndSamplingIntervalAreReported() {
        var server = Filler().Server!;

        server.Port.Should().Be(4841);
        server.GuestAuthentication.Should().BeFalse();
        server.PasswordAuthentication.Should().BeTrue();
        server.StandardServerInterface.Should().BeFalse();
        server.MinimumSamplingInterval.Should().Be(100);
        Markdown(Report()).Should().Contain("| Port | 4841 |").And.Contain("| Guest authentication | no |")
            .And.Contain("| User name and password authentication | yes |").And.Contain("| Standard SIMATIC server interface | no |")
            .And.Contain("| Minimum sampling interval | 100 ms |");
    }

    [Test]
    public void AServerWithoutAnEnableSwitchIsReportedOff() {
        var options = new TiaOpcUaFixture.Options { StoresEnable = false };

        Filler(options).Server!.IsEnabled.Should().BeFalse();
        Markdown(Report(options)).Should().Contain("**Server: off.**");
    }

    [Test]
    public void APlcWithoutAnOpcUaDeviceItemIsSaidToHaveNone() {
        var report = Report();

        report.Plcs.Single(plc => plc.Name == "Capper").Server.Should().BeNull();
        Markdown(report).Should().Contain("## PLC Capper\n\n**Server: no OPC UA device item.**");
    }

    [Test]
    public void ASettingTheReportDoesNotNameIsListedWithItsStoredNameAndValue() =>
        Filler().Server!.OtherSettings.Should().Equal(new TiaOpcUaSetting("SamplingQueue", "7"));

    // What could an OPC UA client reach -------------------------------------------------------------------------------

    [Test]
    public void ADataBlockClosedToOpcUaIsReportedClosed_AndOneThatStoresNoSettingOpen() {
        var blocks = Filler().DataBlocks;

        blocks.Single(block => block.Name == "Service").AccessibleFromOpcUa.Should().BeFalse();
        blocks.Single(block => block.Name == "Recipe").AccessibleFromOpcUa.Should().BeTrue();
    }

    [Test]
    public void ClosedAndReadOnlyMembersAreNamed_AndMembersUnderAClosedStructAreNot() {
        var recipe = Filler().DataBlocks.Single(block => block.Name == "Recipe");

        recipe.HiddenMembers.Should().Equal("Internal", "Hidden");
        recipe.ReadOnlyMembers.Should().Equal("Limit", "Display");
        recipe.Members.Should().Be(7);
        Markdown(Report()).Should().Contain("- Recipe.Hidden: closed\n").And.Contain("- Recipe.Limit: read-only\n").And.NotContain("Inside");
    }

    [Test]
    public void TagsAreReportedWithTheirAccess_AndConstantsAreLeftOut() {
        var tags = Filler().TagTables.Single().Tags;

        tags.Select(tag => (tag.Name, tag.ExternalAccess)).Should().Equal(
            ("Start", TiaExternalAccess.ReadWrite), ("Level", TiaExternalAccess.ReadOnly), ("Spare", TiaExternalAccess.None));
        tags.Single(tag => tag.Name == "Spare").ExternalVisible.Should().BeFalse();
        Markdown(Report()).Should().Contain("| Line tags | 3 | 1 | 1 | 1 | 0 |").And.Contain("| Line tags | Spare | Bool | %M9.0 | not accessible | no |");
    }

    [Test]
    public void AnOffServerIsSaidToReachNoClient_AndWhatWouldBeOpenIsStillGiven() =>
        Markdown(Report(new TiaOpcUaFixture.Options { StoresEnable = false })).Should().Contain(
            "**Server: off.** No client reaches this PLC through its own OPC UA server. Were it on: 1 of 2 data blocks are open to OPC UA, 2 of 3 tags are accessible.");

    // Which server interfaces and roles exist -------------------------------------------------------------------------

    [Test]
    public void RolesAreListedWithNameProjectRoleImportAndPermissions() {
        var storage = Filler().Storage!;

        storage.RolesError.Should().BeNull();
        storage.Roles.Select(role => (role.Name, role.ProjectRole, role.IsImported)).Should().Equal(("Operator", "Line operators", true), ("Viewer", null, false));
        var permission = storage.Roles[0].Permissions.Should().ContainSingle().Subject;
        permission.NamespaceUri.Should().Be("http://www.siemens.com/simatic-s7-opcua");
        permission.Names.Should().Equal("Browse", "ReadRolePermissions", "Read");
        Markdown(Report()).Should().Contain("| Operator | Line operators | yes | http://www.siemens.com/simatic-s7-opcua: Browse, ReadRolePermissions, Read |");
    }

    [Test]
    public void RolesThatCannotBeReadAreReportedSo() {
        var broken = TiaOpcUaFixture.RolesContainer()[..80];

        var storage = Filler(new TiaOpcUaFixture.Options { Roles = broken }).Storage!;

        storage.Roles.Should().BeEmpty();
        storage.RolesError.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void EmptyJournalsNoInterfacesAndTheCompileDateAreReported() {
        var storage = Filler().Storage!;

        storage.HasStructureChanges.Should().BeFalse();
        storage.HasMappingChanges.Should().BeFalse();
        storage.ServerInterfaces.Should().BeEmpty();
        storage.LastCompiled.Should().Be(Compiled);
        storage.LastDownloaded.Should().BeNull();
        Markdown(Report()).Should().Contain("No server interface objects.\nStructure journal: empty. Mapping journal: empty.\nLast compiled: 2026-03-01 08:30 UTC. Last downloaded: never.");
    }

    [Test]
    public void ServerInterfaceObjectsAreListedByName() =>
        Filler(new TiaOpcUaFixture.Options { HasInterface = true }).Storage!.ServerInterfaces.Should().Equal("Line status");

    // Nothing secret comes out ----------------------------------------------------------------------------------------

    [Test]
    public void PasswordsAndCertificateIdsDoNotAppear_AndCertificatesAreOnlyMarkedAndCounted() {
        var server = Filler().Server!;
        var text = Markdown(Report());
        var json = new MemoryStream();
        TiaOpcUaWriter.WriteJson(Report(), json);
        var jsonText = Encoding.UTF8.GetString(json.ToArray());

        server.ServerCertificateAssigned.Should().BeTrue();
        server.TrustedClientCertificates.Should().Be(2);
        foreach (var output in new[] { text, jsonText }) {
            output.Should().NotContain("EncryptedPassword").And.NotContain("not to be seen").And.NotContain("ServerCertificateId").And.NotContain("4,5");
        }
    }

    [Test]
    public void AUserManagementObjectIsNotShownWhenInspected() =>
        Inspected(TiaOpcUaFixture.NewProject(new TiaOpcUaFixture.Options { HasUser = true }), ProjectUser, 32).Should().NotContain("operator.one").And.Contain("not shown");

    [Test]
    public void AnExpandoValueNamedLikeAPasswordIsNotShownWhenInspected() =>
        Inspected(TiaOpcUaFixture.NewProject(), DeviceItem, 20).Should().Contain("~EncryptedPassword1 = [not shown]").And.NotContain("not to be seen")
            .And.Contain("~ServerCertificateId = [not shown]");

    // Read it in a program --------------------------------------------------------------------------------------------

    [Test]
    public void TheJsonReportHoldsTheSameFacts() {
        var json = new MemoryStream();
        TiaOpcUaWriter.WriteJson(Report(), json);

        using var document = JsonDocument.Parse(json.ToArray());
        var filler = document.RootElement.GetProperty("plcs").EnumerateArray().Single(plc => plc.GetProperty("name").GetString() == "Filler");
        filler.GetProperty("server").GetProperty("enabled").GetBoolean().Should().BeTrue();
        filler.GetProperty("server").GetProperty("securityPolicies").GetProperty("256SHA_SE").GetBoolean().Should().BeTrue();
        filler.GetProperty("storage").GetProperty("roles")[0].GetProperty("name").GetString().Should().Be("Operator");
        filler.GetProperty("dataBlocks").EnumerateArray().Select(block => (block.GetProperty("name").GetString(), block.GetProperty("accessibleFromOpcUa").GetBoolean()))
            .Should().BeEquivalentTo([("Recipe", true), ("Service", false)]);
        filler.GetProperty("tagTables")[0].GetProperty("tags").EnumerateArray().Select(tag => tag.GetProperty("access").GetString())
            .Should().Equal("readWrite", "readOnly", "none");
    }

    // Find where a fact lives -----------------------------------------------------------------------------------------

    [Test]
    public void InspectingAnObjectShowsItsExpandoValuesWithTheirKeys() =>
        Inspected(TiaOpcUaFixture.NewProject(), DeviceItem, 20).Should().Contain("~OpcUaEnableServer = 1\n").And.Contain("~ApplicationName = Filler.OPC-UA\n").And.Contain("~MinimumPublishingInterval = 250\n");
}
