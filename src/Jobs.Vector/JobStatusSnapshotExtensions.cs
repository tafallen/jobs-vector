namespace Jobs.Vector;

/// <summary>
/// Provides extension methods for querying metadata from <see cref="JobStatusSnapshot"/>.
/// </summary>
public static class JobStatusSnapshotExtensions
{
    /// <summary>
    /// Retrieves a metadata value cast to the specified type, if it exists.
    /// </summary>
    /// <typeparam name="T">The expected type of the metadata value.</typeparam>
    /// <param name="snapshot">The status snapshot to query.</param>
    /// <param name="key">The metadata dictionary key.</param>
    /// <returns>The typed value if found and of type <typeparamref name="T"/>; otherwise, <c>default</c>.</returns>
    public static T? GetValue<T>(this JobStatusSnapshot snapshot, string key) =>
        snapshot.Metadata.TryGetValue(key, out var value) && value is T typedValue ? typedValue : default;

    /// <summary>
    /// Retrieves a metadata value as an integer, if it exists.
    /// </summary>
    /// <param name="snapshot">The status snapshot to query.</param>
    /// <param name="key">The metadata dictionary key.</param>
    /// <returns>The integer value if found and is an integer; otherwise, <see langword="null"/>.</returns>
    public static int? GetInt(this JobStatusSnapshot snapshot, string key) =>
        snapshot.Metadata.TryGetValue(key, out var value) && value is int i ? i : null;

    /// <summary>
    /// Retrieves a metadata value as a string, if it exists.
    /// </summary>
    /// <param name="snapshot">The status snapshot to query.</param>
    /// <param name="key">The metadata dictionary key.</param>
    /// <returns>The string value if found; otherwise, <see langword="null"/>.</returns>
    public static string? GetString(this JobStatusSnapshot snapshot, string key) =>
        snapshot.Metadata.TryGetValue(key, out var value) ? value as string : null;
}

