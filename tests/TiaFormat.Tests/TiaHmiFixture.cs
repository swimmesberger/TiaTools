using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// An invented project for spec 0011, for what the reference project cannot hold: the HMI device <c>Panel</c> of the
/// project, with one HMI tag linked to a data block whose member cannot be resolved and one of the structured type
/// <c>typeRecipe</c> with a member, and the HMI device <c>CopyPanel</c> held only in a master copy. Kept free of the
/// test framework.
/// </summary>
internal static class TiaHmiFixture {
    private const uint Project = 0x00240001;
    private const uint MasterCopy = 0x00240002;
    private const uint Plc = 0x00240003;
    private const uint Application = 0x00240004;
    private const uint Connection = 0x00240005;
    private const uint TagTable = 0x00240006;
    private const uint Tag = 0x00240007;
    private const uint Link = 0x00240008;
    private const uint DataBlock = 0x00240009;
    private const uint Member = 0x0024000A;

    private const uint Environment = 0x00242001;
    private const uint Target = 0x00242002;
    private const uint Content = 0x00242003;
    private const uint TagConnection = 0x00242004;
    private const uint Consumer = 0x00242005;
    private const uint Provider = 0x00242006;
    private const uint Members = 0x00242007;

    private const string Schema = """
        </Namespace><Namespace name="Siemens.Automation.ObjectFrame">
        <AttributeSet name="ICoreAttributes" id="0x1" persistent="true">
          <Attribute name="Name" id="0" type="xs:string"/><Attribute name="Subtype" id="1" type="xs:string"/><Attribute name="IsKnowHowProtected" id="2" type="xs:boolean"/>
        </AttributeSet>
        </Namespace><Namespace name="Siemens.Automation.DomainServices.CommonTypeSystem">
        <AttributeSet name="IStructureItem" id="0x4" persistent="true"><Attribute name="DisplayTypeName" id="0" type="xs:string"/></AttributeSet>
        </Namespace><Namespace name="Siemens.Automation.DomainModel">
        <ObjectType name="ProjectData" id="0x00240001"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        <ObjectType name="LibraryMasterCopyData" id="0x00240002"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        </Namespace><Namespace name="Siemens.Automation.DomainModel.LinkMaintainer">
        <ObjectType name="ScopedLinkMaintainerData" id="0x00240008">
          <Relation name="Consumer" id="0x00242005" cardinality="*"><Target ref="Siemens.Simatic.Hmi.Utah.Tag.HmiTagData"/></Relation>
          <Relation name="Provider" id="0x00242006" cardinality="*"><Target ref="Siemens.Simatic.Lang.Model.Blocks.DataBlockData"/></Relation>
          <Relation name="Target" id="0x00242002" cardinality="1"><Target ref="Siemens.Simatic.HwConfiguration.Hmi.Model.HmiApplicationData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.HwConfiguration.Model">
        <ObjectType name="S7ControllerTargetData" id="0x00240003">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="Environment" id="0x00242001" cardinality="1"><Target ref="Siemens.Automation.DomainModel.ProjectData"/></Relation>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.HwConfiguration.Hmi.Model">
        <ObjectType name="HmiApplicationData" id="0x00240004">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Hmi.Utah.Communication">
        <ObjectType name="HmiConnectionData" id="0x00240005"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Hmi.Utah.Tag">
        <ObjectType name="HmiTagTableData" id="0x00240006">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Relation name="Content" id="0x00242003" cardinality="*"><Target ref="Siemens.Simatic.Hmi.Utah.Tag.HmiTagData"/></Relation>
        </ObjectType>
        <ObjectType name="HmiTagData" id="0x00240007">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Automation.DomainServices.CommonTypeSystem.IStructureItem"/>
          <Relation name="IHmiTagAttributes_Connection" id="0x00242004" cardinality="1"><Target ref="Siemens.Simatic.Hmi.Utah.Communication.HmiConnectionData"/></Relation>
          <Relation name="Members" id="0x00242007" cardinality="*"><Target ref="Siemens.Simatic.Hmi.Utah.Tag.HmiStructureMemberTagData"/></Relation>
        </ObjectType>
        <ObjectType name="HmiStructureMemberTagData" id="0x0024000A">
          <Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/>
          <Implements ref="Siemens.Automation.DomainServices.CommonTypeSystem.IStructureItem"/>
        </ObjectType>
        </Namespace><Namespace name="Siemens.Simatic.Lang.Model.Blocks">
        <ObjectType name="DataBlockData" id="0x00240009"><Implements ref="Siemens.Automation.ObjectFrame.ICoreAttributes"/></ObjectType>
        </Namespace><Namespace name="N">
        """;

    /// <summary>The project of <see cref="TiaHmiFixture"/>.</summary>
    internal static TiaDatabase NewProject() {
        var file = new TiaFileBuilder(Schema);
        file.WriteObject(Project, 1, [Core("Mixing line")]);
        file.WriteObject(MasterCopy, 2, [Core("Panel template")]);
        file.WriteObject(Plc, 10, [Core("PLC_Mixer"), TiaFileBuilder.Slots((Environment, Project, 1))]);
        file.WriteObject(DataBlock, 20, [Core("Recipe", "DB"), TiaFileBuilder.Slots((Target, Plc, 10), (Environment, Project, 1))]);

        file.WriteObject(Application, 30, [Core("Panel", "HMI.Application.RT.IL"), TiaFileBuilder.Slots((Environment, Project, 1))]);
        file.WriteObject(Connection, 31, [Core("HMI_Connection_1", "Integrated"), TiaFileBuilder.Slots((Target, Application, 30), (Environment, Project, 1))]);
        file.WriteObject(Tag, 32, [Type("Int"), Core("RecipeSpeed"), TiaFileBuilder.Slots((TagConnection, Connection, 31), (Target, Application, 30), (Environment, Project, 1))]);
        file.WriteObject(Member, 36, [Type("Int"), Core("Speed")]);
        file.WriteObject(Tag, 35, [Type("typeRecipe"), Core("RecipeData"), TiaFileBuilder.Slots((TagConnection, Connection, 31), (Members, Member, 36), (Target, Application, 30), (Environment, Project, 1))]);
        file.WriteObject(TagTable, 33, [Core("Recipe"), TiaFileBuilder.Slots((Content, Tag, 32), (Content, Tag, 35), (Target, Application, 30), (Environment, Project, 1))]);
        file.WriteObject(Link, 34, [TiaFileBuilder.Slots((Consumer, Tag, 32), (Provider, DataBlock, 20), (Target, Application, 30))]);

        file.WriteObject(Application, 40, [Core("CopyPanel", "HMI.Application.RT"), TiaFileBuilder.Slots((Environment, MasterCopy, 2))]);
        file.WriteObject(TagTable, 41, [Core("Copied"), TiaFileBuilder.Slots((Target, Application, 40), (Environment, MasterCopy, 2))]);
        file.Commit();
        return TiaDatabase.Parse(file.ToArray());
    }

    /// <summary>IStructureItem: DisplayTypeName by reference.</summary>
    private static byte[] Type(string name) => TiaFileBuilder.Segment(4, (4, TiaFileBuilder.String(name)));

    /// <summary>ICoreAttributes: Name and Subtype by reference, then IsKnowHowProtected.</summary>
    private static byte[] Core(string name, string subtype = "") =>
        TiaFileBuilder.Segment(9, (4, TiaFileBuilder.String(name)), (8, TiaFileBuilder.String(subtype)));
}
