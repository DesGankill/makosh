namespace Makosh.Core;

public static class InboxPaths
{
    public static bool IsBadSegment(string? value)
    {
        if (string.IsNullOrEmpty(value) || value is "." or "..")
        {
            return true;
        }

        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        if (Path.IsPathRooted(value))
        {
            return true;
        }

        return Path.GetFileName(value) != value;
    }

    public static string Destination(string inboxDirectory, string deviceId, string? filename)
    {
        var rawName = string.IsNullOrEmpty(filename) ? "file.bin" : filename;
        if (IsBadSegment(deviceId) || IsBadSegment(rawName))
        {
            throw new InboxPathException();
        }

        var root = Path.GetFullPath(inboxDirectory);
        Directory.CreateDirectory(root);
        var dest = Path.GetFullPath(Path.Combine(root, deviceId, rawName));
        if (!PathGuard.IsInside(dest, root) || dest.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InboxPathException();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        return dest;
    }

    public static string ExistingFile(string inboxDirectory, string deviceId, string name)
    {
        if (IsBadSegment(deviceId) || IsBadSegment(name))
        {
            throw new InboxPathException();
        }

        var root = Path.GetFullPath(inboxDirectory);
        var path = Path.GetFullPath(Path.Combine(root, deviceId, name));
        if (!PathGuard.IsInside(path, root) || path.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InboxPathException();
        }

        return path;
    }
}

public sealed class InboxPathException : Exception
{
    public InboxPathException() : base("Плохой путь")
    {
    }
}
