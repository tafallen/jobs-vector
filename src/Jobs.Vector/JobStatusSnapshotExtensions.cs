namespace Jobs.Vector;

public static class JobStatusSnapshotExtensions
{
    public static int? GetInt(this JobStatusSnapshot snapshot, string key) =>
        snapshot.Metadata.TryGetValue(key, out var value) && value is int i ? i : null;

    public static string? GetString(this JobStatusSnapshot snapshot, string key) =>
        snapshot.Metadata.TryGetValue(key, out var value) ? value as string : null;
}
