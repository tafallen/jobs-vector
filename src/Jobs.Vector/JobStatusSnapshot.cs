using System.Collections.Generic;

namespace Jobs.Vector;

/// <summary>
/// Represents a snapshot of the current state of a background job.
/// </summary>
/// <param name="Status">The current status of the job.</param>
/// <param name="Progress">The execution progress of the job, typically from 0 to 100.</param>
/// <param name="Error">Optional error message if the job failed.</param>
/// <param name="Metadata">Metadata associated with the job (e.g. execution results or stats).</param>
public record JobStatusSnapshot(
    JobStatus Status,
    int Progress,
    string? Error,
    IReadOnlyDictionary<string, object> Metadata);

