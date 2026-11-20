using System.Globalization;
using System.Text;
using System.Xml;

namespace TiaFormat.Program;

/// <summary>
/// Writes a PLC's nodes as an OPC UA node set file (UANodeSet XML, OPC UA Part 6), laid out as TIA Portal V21's export
/// is: the same namespaces, aliases, node ids, references and attributes. Data, object and variable type nodes are
/// referred to, not written (spec 0006).
/// </summary>
public static class TiaOpcUaNodeSet {
    private const string UaNodeSet = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

    /// <summary>The reference types the file uses, by the alias TIA's export gives them.</summary>
    private static readonly Dictionary<string, string> References = new(StringComparer.Ordinal) {
        ["Organizes"] = "i=35", ["HasTypeDefinition"] = "i=40", ["HasComponent"] = "i=47", ["HasInOutVar"] = "ns=3;i=4002",
        ["HasInputVar"] = "ns=3;i=4003", ["HasOutputVar"] = "ns=3;i=4004", ["HasLocalVar"] = "ns=3;i=4005",
    };

    public static void Write(TiaOpcUaNodeList list, Stream output) {
        var settings = new XmlWriterSettings { Indent = true, IndentChars = "    ", NewLineChars = "\n", Encoding = new UTF8Encoding(false) };
        using var xml = XmlWriter.Create(output, settings);
        xml.WriteStartDocument();
        xml.WriteComment(" " + TiaOpcUaNodes.Derived + " ");
        xml.WriteStartElement("UANodeSet", UaNodeSet);

        // Index 1 is left empty, as in TIA's export, so that ns=2 is DI and ns=3 the Siemens namespace, as on the server.
        xml.WriteStartElement("NamespaceUris", UaNodeSet);
        xml.WriteElementString("Uri", UaNodeSet, "");
        xml.WriteElementString("Uri", UaNodeSet, "http://opcfoundation.org/UA/DI/");
        xml.WriteElementString("Uri", UaNodeSet, TiaOpcUaNodes.Namespace);
        xml.WriteEndElement();

        xml.WriteStartElement("Aliases", UaNodeSet);
        var aliases = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in list.Nodes) {
            if (node.DataTypeName is { } name && node.DataType is { } id && name != id && !name.StartsWith("DT_", StringComparison.Ordinal)) {
                aliases[name] = id;
            }
        }
        foreach (var (alias, id) in aliases.Concat(References.OrderBy(pair => pair.Key, StringComparer.Ordinal))) {
            xml.WriteStartElement("Alias", UaNodeSet);
            xml.WriteAttributeString("Alias", alias);
            xml.WriteString(id);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();

        foreach (var node in list.Nodes) {
            var variable = node.NodeClass == TiaOpcUaNodeClass.Variable;
            xml.WriteStartElement(variable ? "UAVariable" : "UAObject", UaNodeSet);
            xml.WriteAttributeString("NodeId", node.NodeId);
            xml.WriteAttributeString("BrowseName", "3:" + node.BrowseName);
            xml.WriteAttributeString("ParentNodeId", node.ParentNodeId);
            if (variable) {
                if (node.DataType is { } dataType) {
                    xml.WriteAttributeString("DataType", node.DataTypeName is { } name && !name.StartsWith("DT_", StringComparison.Ordinal) ? name : dataType);
                }
                if (node.IsWritable) {
                    xml.WriteAttributeString("AccessLevel", "3");
                }
                if (node.ArrayDimensions is { } dimensions) {
                    xml.WriteAttributeString("ValueRank", Math.Max(1, dimensions.Count).ToString(CultureInfo.InvariantCulture));
                    if (dimensions.Count != 0) {
                        xml.WriteAttributeString("ArrayDimensions", string.Join(",", dimensions.Select(d => d.ToString(CultureInfo.InvariantCulture))));
                    }
                }
            }
            xml.WriteElementString("DisplayName", UaNodeSet, node.BrowseName);
            if (node.Description is { } description) {
                xml.WriteElementString("Description", UaNodeSet, description);
            }
            xml.WriteStartElement("References", UaNodeSet);
            Reference(xml, node.ParentReference, node.ParentNodeId, forward: false);
            if (node.NodeId == "ns=3;s=PLC") {
                Reference(xml, "Organizes", "i=85", forward: false);
            }
            if (node.TypeDefinition is { } typeDefinition) {
                Reference(xml, "HasTypeDefinition", typeDefinition, forward: true);
            }
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    private static void Reference(XmlWriter xml, string type, string target, bool forward) {
        xml.WriteStartElement("Reference", UaNodeSet);
        xml.WriteAttributeString("ReferenceType", type);
        if (!forward) {
            xml.WriteAttributeString("IsForward", "false");
        }
        xml.WriteString(target);
        xml.WriteEndElement();
    }
}
