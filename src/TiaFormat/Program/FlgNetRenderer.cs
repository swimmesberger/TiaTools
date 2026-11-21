namespace TiaFormat.Program;

/// <summary>Writes one stored LAD or FBD network in statement form (ADR 0013), through <see cref="TiaFlgNet"/> and <see cref="NetworkStatements"/>.</summary>
public static class FlgNetRenderer {
    public static void Render(ReadOnlySpan<byte> xml, TiaIdentTable idents, PooledByteWriter output) =>
        NetworkStatements.Write(TiaFlgNet.Read(xml, idents), output);
}
