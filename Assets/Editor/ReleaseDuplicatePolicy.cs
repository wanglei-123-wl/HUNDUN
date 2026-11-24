using System.Linq;

public static class ReleaseDuplicatePolicy
{
    // Keep these two measured engine/bake placeholders visible in reports.
    // They are not business art; larger or different objects still fail validation.
    public static bool IsSmallMetadata(string guid, long[] objectIds, ulong extraBytes)
    {
        if (objectIds == null || objectIds.Length != 1) return false;
        return (guid == "0000000000000000f000000000000000" && objectIds[0] == 10304 && extraBytes <= 1024)
            || (guid == "424a8351358298f49830eb6d76d863c4" && objectIds[0] == 4890085278179872738 && extraBytes <= 128);
    }
}
