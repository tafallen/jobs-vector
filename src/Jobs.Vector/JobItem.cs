namespace Jobs.Vector;

public record JobItem(string JobId, Func<CancellationToken, Task> Job);
