namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Identifies the type of file copy engine being used.
/// </summary>
public enum CopyEngineType
{
    /// <summary>
    /// Automatically select the best engine based on copy scenario.
    /// The system will analyze source/destination paths, file counts, network status,
    /// and select the optimal engine (usually Native for local, Robocopy for network).
    /// </summary>
    Auto,
    
    /// <summary>
    /// Use Windows robocopy.exe command-line tool.
    /// Best for: Network copies, complex filtering, established reliability.
    /// Requires: robocopy.exe available (included in Windows Vista+)
    /// </summary>
    Robocopy,
    
    /// <summary>
    /// Use native C# file copy implementation with async I/O.
    /// Best for: Local SSD copies, when robocopy.exe not available.
    /// Requires: .NET runtime only
    /// Performance: 2-4x faster than robocopy for local operations.
    /// </summary>
    Native,
    
    // Future engines can be added here:
    // Rsync,     // Linux/Unix rsync tool (for cross-platform)
    // Rclone,    // Cloud storage sync (S3, Azure, etc.)
    // FastCopy,  // Third-party Windows tool integration
    // BitTorrent // Distributed peer-to-peer copying
}
