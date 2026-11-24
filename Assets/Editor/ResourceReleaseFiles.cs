using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

public static class ResourceReleaseFiles
{
    public static string FindBundleFile(string logicalName, IEnumerable<string> files)
    {
        string name = logicalName.Replace('\\', '/');
        if (Path.IsPathRooted(logicalName) || name.Split('/').Any(part => part == "." || part == ".."))
            throw new InvalidDataException("Invalid logical bundle name: " + logicalName);
        string[] matches = files.Where(path => path.Replace('\\', '/').EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Built bundle did not resolve uniquely: " + logicalName);
        return matches[0];
    }

    public static string FindBundleName(string internalId, IEnumerable<string> names)
    {
        string path = internalId.Replace('\\', '/');
        string name = names.Where(item => path.EndsWith("/" + item.Replace('\\', '/'), StringComparison.Ordinal))
            .OrderByDescending(item => item.Length).FirstOrDefault();
        if (name == null) throw new InvalidDataException("Catalog bundle path is not in the layout: " + internalId);
        return name;
    }

    public static string Inside(string parent, string path)
    {
        string boundary = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path escapes its expected directory: " + full);
        return full;
    }

    public static string Hash(string path)
    {
        using (var algorithm = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    public static void CopyTree(string source, string destination)
    {
        if (Directory.Exists(destination)) throw new IOException("Refusing to overwrite " + destination);
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = path.Substring(Path.GetFullPath(source).TrimEnd('\\', '/').Length + 1);
            string target = Inside(destination, Path.Combine(destination, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(path, target, false);
        }
    }
}
