using Makosh.Core;

namespace Makosh.Tests;

public class InboxPathsTests
{
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("foo/bar.txt")]
    [InlineData("foo\\bar.txt")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share")]
    public void Rejects_bad_segments(string value)
    {
        Assert.True(InboxPaths.IsBadSegment(value));
    }

    [Theory]
    [InlineData("note.txt")]
    [InlineData("a b (1).txt")]
    [InlineData("phone")]
    public void Accepts_plain_names(string value)
    {
        Assert.False(InboxPaths.IsBadSegment(value));
    }

    [Fact]
    public void Destination_creates_file_inside_inbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "makosh-inbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dest = InboxPaths.Destination(root, "phone", "note.txt");
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "phone", "note.txt"), dest);
            Assert.True(Directory.Exists(Path.GetDirectoryName(dest)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Destination_uses_file_bin_when_name_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "makosh-inbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dest = InboxPaths.Destination(root, "phone", "");
            Assert.EndsWith(Path.Combine("phone", "file.bin"), dest, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Destination_rejects_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "makosh-inbox-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InboxPathException>(() => InboxPaths.Destination(root, "phone", "../x.txt"));
        Assert.Throws<InboxPathException>(() => InboxPaths.Destination(root, "..", "x.txt"));
    }
}
