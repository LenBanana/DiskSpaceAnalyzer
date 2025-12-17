using System;
using System.IO;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
/// Utility class for analyzing file copy scenarios and path characteristics.
/// Provides path analysis, drive type detection, and performance estimation
/// to support intelligent engine selection.
/// </summary>
public static class CopyScenarioAnalyzer
{
    /// <summary>
    /// Determines if a path points to a network location.
    /// Detects UNC paths (\\server\share) and mapped network drives.
    /// </summary>
    /// <param name="path">The path to analyze.</param>
    /// <returns>True if the path is a network location, false otherwise.</returns>
    public static bool IsNetworkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            // UNC path detection (\\server\share\path)
            if (path.StartsWith(@"\\") || path.StartsWith("//"))
                return true;

            // Get the root of the path (e.g., "C:\")
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return false;

            // Check if it's a mapped drive pointing to a network location
            var driveInfo = new DriveInfo(root);
            return driveInfo.DriveType == DriveType.Network;
        }
        catch
        {
            // If we can't determine, assume local (safer default)
            return false;
        }
    }

    /// <summary>
    /// Gets the drive type for a given path.
    /// Useful for performance estimation and engine selection.
    /// </summary>
    /// <param name="path">The path to analyze.</param>
    /// <returns>
    /// The DriveType enum value, or null if the drive type cannot be determined.
    /// </returns>
    public static DriveType? GetDriveType(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            // UNC paths are always network
            if (path.StartsWith(@"\\") || path.StartsWith("//"))
                return DriveType.Network;

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return null;

            var driveInfo = new DriveInfo(root);
            return driveInfo.DriveType;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if two paths are on the same physical drive.
    /// Useful for detecting same-drive copies (which can be faster).
    /// </summary>
    /// <param name="path1">First path to compare.</param>
    /// <param name="path2">Second path to compare.</param>
    /// <returns>True if both paths are on the same drive, false otherwise.</returns>
    public static bool IsSameDrive(string path1, string path2)
    {
        if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2))
            return false;

        try
        {
            var root1 = Path.GetPathRoot(path1);
            var root2 = Path.GetPathRoot(path2);

            if (string.IsNullOrEmpty(root1) || string.IsNullOrEmpty(root2))
                return false;

            // Compare roots (case-insensitive for Windows)
            return string.Equals(root1, root2, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Estimates the optimal degree of parallelism based on drive types.
    /// SSDs benefit from higher parallelism, HDDs and network drives from moderate.
    /// </summary>
    /// <param name="sourceDriveType">Source drive type.</param>
    /// <param name="destDriveType">Destination drive type.</param>
    /// <returns>Recommended number of parallel operations (2-16).</returns>
    public static int EstimateOptimalParallelism(DriveType? sourceDriveType, DriveType? destDriveType)
    {
        // Conservative default if we can't determine
        if (!sourceDriveType.HasValue || !destDriveType.HasValue)
            return 8;

        // Network operations: moderate parallelism (avoid overwhelming network)
        if (sourceDriveType == DriveType.Network || destDriveType == DriveType.Network)
            return 4;

        // Removable drives (USB, etc.): lower parallelism
        if (sourceDriveType == DriveType.Removable || destDriveType == DriveType.Removable)
            return 4;

        // Fixed drives (HDD or SSD): assume SSD-like for performance
        // Modern systems mostly use SSDs, so we lean toward higher parallelism
        if (sourceDriveType == DriveType.Fixed && destDriveType == DriveType.Fixed)
            return 12; // High parallelism for likely SSD-to-SSD

        // Mixed scenarios: balanced approach
        return 8;
    }

    /// <summary>
    /// Checks if copying a directory into itself (which would be problematic).
    /// </summary>
    /// <param name="sourcePath">Source directory path.</param>
    /// <param name="destinationPath">Destination directory path.</param>
    /// <returns>True if destination is inside source, false otherwise.</returns>
    public static bool IsDestinationInsideSource(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            return false;

        try
        {
            var sourceFullPath = Path.GetFullPath(sourcePath);
            var destFullPath = Path.GetFullPath(destinationPath);

            // Ensure paths end with directory separator for proper comparison
            if (!sourceFullPath.EndsWith(Path.DirectorySeparatorChar))
                sourceFullPath += Path.DirectorySeparatorChar;

            // Check if destination starts with source path
            return destFullPath.StartsWith(sourceFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Analyzes path accessibility and returns potential issues.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>
    /// Tuple containing:
    /// - Exists: Whether the path exists
    /// - IsAccessible: Whether the path can be accessed
    /// - ErrorMessage: Description of any issues, or null if none
    /// </returns>
    public static (bool Exists, bool IsAccessible, string? ErrorMessage) AnalyzePathAccessibility(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, false, "Path is empty or null");

        try
        {
            if (!Directory.Exists(path))
                return (false, false, "Directory does not exist");

            // Try to enumerate to check access
            _ = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
            return (true, true, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (true, false, "Access denied - insufficient permissions");
        }
        catch (PathTooLongException)
        {
            return (false, false, "Path is too long");
        }
        catch (Exception ex)
        {
            return (true, false, $"Access error: {ex.Message}");
        }
    }

    /// <summary>
    /// Provides a human-readable description of a drive type.
    /// </summary>
    /// <param name="driveType">The drive type to describe.</param>
    /// <returns>User-friendly description string.</returns>
    public static string GetDriveTypeDescription(DriveType driveType)
    {
        return driveType switch
        {
            DriveType.Fixed => "Local Drive (HDD/SSD)",
            DriveType.Network => "Network Drive",
            DriveType.Removable => "Removable Drive (USB, External)",
            DriveType.CDRom => "CD/DVD Drive",
            DriveType.Ram => "RAM Disk",
            DriveType.NoRootDirectory => "Unknown Drive Type",
            DriveType.Unknown => "Unknown Drive Type",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Estimates the performance category for a drive type.
    /// Used for engine selection and parallelism tuning.
    /// </summary>
    /// <param name="driveType">The drive type to evaluate.</param>
    /// <returns>Performance category: "High", "Medium", "Low", or "Unknown".</returns>
    public static string GetDrivePerformanceCategory(DriveType driveType)
    {
        return driveType switch
        {
            DriveType.Fixed => "High", // Assumes modern SSD or fast HDD
            DriveType.Network => "Medium", // Network-limited
            DriveType.Removable => "Medium", // USB 3.0+, varies
            DriveType.CDRom => "Low", // Optical media is slow
            DriveType.Ram => "High", // RAM is very fast
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Determines if the scenario benefits from native engine's speed advantage.
    /// Native engine is 2-4x faster for local SSD operations.
    /// </summary>
    /// <param name="sourcePath">Source path.</param>
    /// <param name="destinationPath">Destination path.</param>
    /// <returns>True if native engine would provide significant speed benefit.</returns>
    public static bool WouldBenefitFromNativeSpeed(string sourcePath, string destinationPath)
    {
        // Network paths don't benefit (robocopy's retry logic is more valuable)
        if (IsNetworkPath(sourcePath) || IsNetworkPath(destinationPath))
            return false;

        var sourceDriveType = GetDriveType(sourcePath);
        var destDriveType = GetDriveType(destinationPath);

        // Local fixed drives (SSD/HDD) benefit most from native's speed
        if (sourceDriveType == DriveType.Fixed && destDriveType == DriveType.Fixed)
            return true;

        // Removable to removable or fixed also benefits
        if (sourceDriveType == DriveType.Removable && destDriveType == DriveType.Fixed)
            return true;

        if (sourceDriveType == DriveType.Fixed && destDriveType == DriveType.Removable)
            return true;

        // Other scenarios: marginal benefit
        return false;
    }

    /// <summary>
    /// Checks if robocopy.exe is likely to be more reliable for this scenario.
    /// Robocopy excels at network copies, complex filtering, and locked files.
    /// </summary>
    /// <param name="sourcePath">Source path.</param>
    /// <param name="destinationPath">Destination path.</param>
    /// <param name="requiresBackupMode">Whether backup mode is required.</param>
    /// <param name="requiresSecurity">Whether security info copying is required.</param>
    /// <returns>True if robocopy would be more reliable.</returns>
    public static bool WouldBenefitFromRobocopyReliability(
        string sourcePath,
        string destinationPath,
        bool requiresBackupMode,
        bool requiresSecurity)
    {
        // Backup mode: only robocopy supports this
        if (requiresBackupMode)
            return true;

        // Security info: robocopy has better support
        if (requiresSecurity)
            return true;

        // Network paths: robocopy's retry logic is valuable
        if (IsNetworkPath(sourcePath) || IsNetworkPath(destinationPath))
            return true;

        // Otherwise, native is fine
        return false;
    }
}
