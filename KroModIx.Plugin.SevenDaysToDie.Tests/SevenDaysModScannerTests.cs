using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using KroModIx.Plugin.SevenDaysToDie.Services;
using Xunit;

namespace KroModIx.Plugin.SevenDaysToDie.Tests;

/// <summary>Regression-Guard fuer den 7DTD-Mod-Scanner. Fokussiert
/// auf den XML-Parser (beide historischen 7DTD-Formate) — der Directory-
/// Scan haengt an einem DetectedGame und ist trivial.</summary>
public class SevenDaysModScannerTests
{
    [Fact]
    public void ParseModInfo_A20AttributeFormat()
    {
        var xml = """
        <?xml version="1.0" encoding="UTF-8" ?>
        <xml>
            <Name value="MyCoolMod" />
            <Author value="Kroste" />
            <Version value="1.2.3" />
            <Description value="Ein cooler Mod." />
        </xml>
        """;
        using var tmp = new TempFile(xml);
        var (name, author, version, desc) = SevenDaysModScanner.ParseModInfo(tmp.Path, "fallback");
        name.Should().Be("MyCoolMod");
        author.Should().Be("Kroste");
        version.Should().Be("1.2.3");
        desc.Should().Be("Ein cooler Mod.");
    }

    [Fact]
    public void ParseModInfo_LegacyElementFormat()
    {
        var xml = """
        <xml>
            <Name>LegacyMod</Name>
            <Author>SomeoneElse</Author>
            <Version>0.4</Version>
        </xml>
        """;
        using var tmp = new TempFile(xml);
        var (name, author, version, desc) = SevenDaysModScanner.ParseModInfo(tmp.Path, "fallback");
        name.Should().Be("LegacyMod");
        author.Should().Be("SomeoneElse");
        version.Should().Be("0.4");
        desc.Should().BeNull();
    }

    [Fact]
    public void ParseModInfo_MissingFieldsUsesFallbackName()
    {
        var xml = "<xml><OtherField value=\"x\" /></xml>";
        using var tmp = new TempFile(xml);
        var (name, author, version, desc) = SevenDaysModScanner.ParseModInfo(tmp.Path, "FolderName");
        name.Should().Be("FolderName");
        author.Should().BeNull();
        version.Should().BeNull();
        desc.Should().BeNull();
    }

    [Fact]
    public void ParseModInfo_MalformedFileFallsBackGracefully()
    {
        using var tmp = new TempFile("<not-well-formed<");
        var (name, _, _, _) = SevenDaysModScanner.ParseModInfo(tmp.Path, "fallback");
        name.Should().Be("fallback");
    }

    private sealed class TempFile : System.IDisposable
    {
        public string Path { get; }
        public TempFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "7dtd-modinfo-" + System.Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(Path, content);
        }
        public void Dispose() { try { File.Delete(Path); } catch { } }
    }
}
