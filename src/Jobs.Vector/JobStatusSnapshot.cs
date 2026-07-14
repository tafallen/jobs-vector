using System.Collections.Generic;

namespace Jobs.Vector;

public record JobStatusSnapshot(
    JobStatus Status,
    int Progress,
    string? Error,
    IReadOnlyDictionary<string, object> Metadata);
