using System.Collections.Generic;
using DiskSpaceAnalyzer.Models.Robocopy;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
///     Builds robocopy command line arguments from options.
///     Separated for testability and clarity.
/// </summary>
public class RobocopyCommandBuilder
{
    /// <summary>
    ///     Build complete robocopy command line arguments.
    /// </summary>
    public string BuildArguments(RobocopyOptions options)
    {
        var args = new List<string>();

        // Source and destination (quoted if they contain spaces)
        args.Add(QuotePath(options.SourcePath));
        args.Add(QuotePath(options.DestinationPath));

        // File selection (wildcards)
        if (options.IncludeFiles.Count > 0)
            foreach (var file in options.IncludeFiles)
                args.Add(file);
        else
            args.Add("*.*"); // Default: copy all files

        // Copy subdirectories
        if (options.CopySubdirectories) args.Add("/E"); // Copy subdirectories including empty ones

        // Mirror mode (DANGEROUS - deletes at destination)
        if (options.MirrorMode) args.Add("/MIR"); // Mirror mode

        // Multi-threading
        if (options.UseMultithreading) args.Add($"/MT:{options.ThreadCount}");

        // Retry options
        args.Add($"/R:{options.RetryCount}");
        args.Add($"/W:{options.RetryWaitSeconds}");

        // Copy flags
        if (!string.IsNullOrEmpty(options.CopyFlags)) args.Add($"/COPY:{options.CopyFlags}");

        // Backup mode
        if (options.BackupMode) args.Add("/B");

        // Copy all (security, etc.)
        if (options.CopyAll) args.Add("/COPYALL");

        // Security
        if (options.CopySecurity) args.Add("/SEC");

        // Symbolic links
        if (options.CopySymbolicLinks) args.Add("/SL");

        // Create tree only
        if (options.CreateTreeOnly) args.Add("/CREATE");

        // Exclude options
        if (options.ExcludeOlder) args.Add("/XO");

        if (options.ExcludeNewer) args.Add("/XN");

        // Move options
        if (options.MoveFiles)
            args.Add("/MOV");
        else if (options.MoveFilesAndDirectories) args.Add("/MOVE");

        // Exclude directories
        if (options.ExcludeDirectories.Count > 0)
        {
            args.Add("/XD");
            foreach (var dir in options.ExcludeDirectories)
                args.Add(QuotePath(dir));
        }

        // Exclude files
        if (options.ExcludeFiles.Count > 0)
        {
            args.Add("/XF");
            foreach (var file in options.ExcludeFiles)
                args.Add(QuotePath(file)); // Quote in case pattern has spaces
        }

        // File size filters
        if (options.MaxFileSize.HasValue) args.Add($"/MAX:{options.MaxFileSize.Value}");

        if (options.MinFileSize.HasValue) args.Add($"/MIN:{options.MinFileSize.Value}");

        // Logging options
        if (!string.IsNullOrEmpty(options.LogFilePath)) args.Add($"/LOG:{QuotePath(options.LogFilePath)}");

        // Output options for better parsing
        args.Add("/NP"); // No progress percentage (cleaner output)
        args.Add("/V"); // Verbose output (shows files being copied)
        args.Add("/TS"); // Include timestamps
        args.Add("/FP"); // Include full path names
        args.Add("/BYTES"); // Show sizes in bytes
        args.Add("/TEE"); // Output to console AND log file

        return string.Join(" ", args);
    }

    /// <summary>
    ///     Quote a path if it contains spaces.
    /// </summary>
    private string QuotePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Already quoted
        if (path.StartsWith("\"") && path.EndsWith("\""))
            return path;

        // Quote if contains spaces
        if (path.Contains(" "))
            return $"\"{path}\"";

        return path;
    }

    /// <summary>
    ///     Build a dry-run command (list only, no copying).
    /// </summary>
    public string BuildDryRunArguments(RobocopyOptions options)
    {
        var args = BuildArguments(options);
        return args + " /L"; // /L = List only (dry run)
    }
}