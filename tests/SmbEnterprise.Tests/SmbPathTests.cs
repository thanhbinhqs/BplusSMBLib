using SmbEnterprise.Core.Paths;

namespace SmbEnterprise.Tests;

public class SmbPathTests
{
    [Theory]
    [InlineData(@"\\server\share", "server", "share", "")]
    [InlineData(@"\\server\share\folder\file.txt", "server", "share", @"\folder\file.txt")]
    [InlineData(@"\\server\share\deep\nested\path", "server", "share", @"\deep\nested\path")]
    public void Parse_ValidUncPath_ExtractsComponents(string path, string server, string share, string relativePath)
    {
        var smbPath = SmbPath.Parse(path);

        Assert.Equal(server, smbPath.Server);
        Assert.Equal(share, smbPath.Share);
        Assert.Equal(relativePath, smbPath.RelativePath);
    }

    [Fact]
    public void Parse_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SmbPath.Parse(""));
    }

    [Fact]
    public void Combine_AppendRelativePath()
    {
        var path = SmbPath.Parse(@"\\server\share\folder");
        var combined = path.Combine("subfolder").Combine("file.txt");

        Assert.Equal("file.txt", combined.FileName);
    }

    [Fact]
    public void Parent_ReturnsParentDirectory()
    {
        var path = SmbPath.Parse(@"\\server\share\folder\file.txt");
        var parent = path.Parent();

        Assert.NotNull(parent);
        Assert.Equal(@"\folder", parent!.RelativePath);
    }

    [Fact]
    public void ToUncPath_ReturnsCorrectFormat()
    {
        var path = SmbPath.Parse(@"\\server\share\folder\file.txt");
        Assert.Equal(@"\\server\share\folder\file.txt", path.ToUncPath());
    }

    [Fact]
    public void FileName_RootPath_ReturnsEmptyString()
    {
        var path = SmbPath.Parse(@"\\server\share");
        Assert.Equal("", path.FileName);
    }
}
