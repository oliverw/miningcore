namespace Miningcore.Api.Responses;

public class AdminGcStats
{
    /// <summary>
    /// Number of Generation 0 collections
    /// </summary>
    public int GcGen0 { get; set; }

    /// <summary>
    /// Number of Generation 1 collections
    /// </summary>
    public int GcGen1 { get; set; }

    /// <summary>
    /// Number of Generation 2 collections
    /// </summary>
    public int GcGen2 { get; set; }

    /// <summary>
    /// Assumed amount of allocated memory
    /// </summary>
    public string MemAllocated { get; set; }

    /// <summary>
    /// Maximum time in seconds spent in full GC
    /// </summary>
    public double MaxFullGcDuration { get; set; } = 0;
}
