using System.Reflection;
using OutSystems.ExternalLibraries.SDK;

namespace OdcAgentFileInspector.Tests;

/// <summary>
/// ODC rejects the upload (OS-ELG-MODL-05009) when IconResourceName does not match an embedded resource.
/// </summary>
[TestFixture]
public class IconResourceTests
{
    [Test]
    public void IconResourceName_MatchesAnEmbeddedPngResource()
    {
        var attribute = typeof(IAgentFileInspector).GetCustomAttribute<OSInterfaceAttribute>();
        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute!.IconResourceName, Is.Not.Empty);

        var assembly = typeof(IAgentFileInspector).Assembly;
        using var stream = assembly.GetManifestResourceStream(attribute.IconResourceName!);
        Assert.That(stream, Is.Not.Null,
            $"No embedded resource named '{attribute.IconResourceName}'. Found: {string.Join(", ", assembly.GetManifestResourceNames())}");

        var header = new byte[8];
        Assert.That(stream!.Read(header, 0, 8), Is.EqualTo(8));
        Assert.That(header, Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "Icon is not a PNG.");
    }
}
