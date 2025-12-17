using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DiskSpaceAnalyzer.Models.Robocopy;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
/// Parses robocopy log files to extract progress and error information.
/// Designed to handle robocopy's varied output formats.
/// </summary>
public partial class RobocopyLogParser
{
    // Regex patterns for parsing (compiled for performance)
    [GeneratedRegex(@"^\s*ERROR\s+(\d+)\s+\(0x([0-9A-F]+)\)\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLineRegex();
    
    [GeneratedRegex(@"^\s+\d+\s+(.+)$", RegexOptions.Compiled)]
    private static partial Regex FileLineRegex();
    
    [GeneratedRegex(@"^\s+Dirs\s*:\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DirsSummaryRegex();
    
    [GeneratedRegex(@"^\s+Files\s*:\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex FilesSummaryRegex();
    
    [GeneratedRegex(@"^\s+Bytes\s*:\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BytesSummaryRegex();
    
    /// <summary>
    /// Parse errors from log file lines.
    /// </summary>
    public List<RobocopyError> ParseErrors(IEnumerable<string> logLines)
    {
        var errors = new List<RobocopyError>();
        
        foreach (var line in logLines)
        {
            var match = ErrorLineRegex().Match(line);
            if (match.Success)
            {
                errors.Add(new RobocopyError
                {
                    ErrorCode = int.Parse(match.Groups[1].Value),
                    HexCode = "0x" + match.Groups[2].Value,
                    Message = match.Groups[3].Value.Trim(),
                    FilePath = ExtractFilePathFromErrorMessage(match.Groups[3].Value),
                    Timestamp = DateTime.Now
                });
            }
        }
        
        return errors;
    }
    
    /// <summary>
    /// Extract file path from error message (files are usually at the end).
    /// </summary>
    private string ExtractFilePathFromErrorMessage(string message)
    {
        // Error messages often end with the filename
        // Example: "Copying File C:\path\to\file.txt"
        // Example: "The filename or extension is too long. file.txt"
        
        var parts = message.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            var lastPart = parts[^1];
            if (lastPart.Contains("\\") || lastPart.Contains("/"))
                return lastPart;
        }
        
        return string.Empty;
    }
    
    /// <summary>
    /// Parse the final summary statistics from robocopy log.
    /// Robocopy outputs a summary section like:
    ///   Dirs : Total Copied Skipped Mismatch FAILED Extras
    ///   Files: Total Copied Skipped Mismatch FAILED Extras
    ///   Bytes: Total Copied Skipped Mismatch FAILED Extras
    /// </summary>
    public RobocopyResult ParseSummary(string logContent, RobocopyResult result)
    {
        var lines = logContent.Split('\n');
        
        foreach (var line in lines)
        {
            // Parse Dirs line
            var dirsMatch = DirsSummaryRegex().Match(line);
            if (dirsMatch.Success)
            {
                result.TotalDirectories = ParseLong(dirsMatch.Groups[1].Value);
                result.DirectoriesCopied = ParseLong(dirsMatch.Groups[2].Value);
                // Groups 3-6 are skipped, mismatch, failed, extras
            }
            
            // Parse Files line
            var filesMatch = FilesSummaryRegex().Match(line);
            if (filesMatch.Success)
            {
                result.TotalFiles = ParseLong(filesMatch.Groups[1].Value);
                result.FilesCopied = ParseLong(filesMatch.Groups[2].Value);
                result.FilesSkipped = ParseLong(filesMatch.Groups[3].Value);
                result.FilesMismatched = ParseLong(filesMatch.Groups[4].Value);
                result.FilesFailed = ParseLong(filesMatch.Groups[5].Value);
                result.FilesExtra = ParseLong(filesMatch.Groups[6].Value);
            }
            
            // Parse Bytes line
            var bytesMatch = BytesSummaryRegex().Match(line);
            if (bytesMatch.Success)
            {
                result.TotalBytes = ParseLong(bytesMatch.Groups[1].Value);
                result.BytesCopied = ParseLong(bytesMatch.Groups[2].Value);
                // Groups 3-6 are skipped, mismatch, failed, extras bytes
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Parse current file being processed from recent log lines.
    /// With /V flag, format is: "  Status  Size  FullPath"
    /// </summary>
    public string? ParseCurrentFile(IEnumerable<string> recentLines)
    {
        string? lastFile = null;
        
        foreach (var line in recentLines)
        {
            // Skip empty lines and section headers
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            // Look for file operation lines (New File, Newer, etc.)
            if ((line.Contains("New File") || 
                 line.Contains("Newer") ||
                 line.Contains("Older") ||
                 line.Contains("*EXTRA File") ||
                 line.Contains("same")) && 
                !line.StartsWith("ERROR"))
            {
                // Parse format: "  Status  Size  FullPath"
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                
                // Path is usually the last element
                if (parts.Length >= 3)
                {
                    lastFile = parts[^1]; // Last element is the path
                }
            }
            // Fallback: any line with a file path
            else if ((line.Contains("\\") || line.Contains("/")) && !line.StartsWith("ERROR"))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    // Extract the path part
                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    lastFile = parts[^1];
                }
            }
        }
        
        return lastFile;
    }
    
    /// <summary>
    /// Parse file information (path and size) from a robocopy output line.
    /// Returns null if the line doesn't contain file copy information.
    /// </summary>
    public (string? filePath, long fileSize) ParseFileInfo(string line)
    {
        // Look for file operation lines with size information
        // Format with /V /TS /FP /BYTES flags:
        // "    New File  		    1091 2024/08/20 07:04:36	C:\Angular Projects\file.txt"
        if ((line.Contains("New File") || 
             line.Contains("Newer") ||
             line.Contains("Older") ||
             line.Contains("same")) && 
            !line.StartsWith("ERROR"))
        {
            try
            {
                // Split by whitespace to get individual tokens
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length < 5) // Need at least: status, status2(File), size, date, time, path
                    return (null, 0);
                
                // Find the file size (first number after status)
                int sizeIndex = -1;
                long fileSize = 0;
                
                for (int i = 0; i < parts.Length; i++)
                {
                    if (long.TryParse(parts[i], out fileSize))
                    {
                        sizeIndex = i;
                        break;
                    }
                }
                
                if (sizeIndex < 0)
                    return (null, 0);
                
                // After size, expect timestamp (date and time), then the path
                // Date format: 2024/08/20 or similar
                // Time format: 07:04:36 or similar
                // So skip 2 elements after size (date + time) to get to the path
                int pathStartIndex = sizeIndex + 3; // size + date + time = path starts here
                
                if (pathStartIndex >= parts.Length)
                    return (null, 0);
                
                // Everything from pathStartIndex onwards is the file path
                // (in case path has spaces, we need to rejoin)
                var filePath = string.Join(" ", parts.Skip(pathStartIndex));
                
                // Validate the path looks reasonable (has drive letter or UNC path)
                if (filePath.Length > 2 && 
                    (filePath[1] == ':' || filePath.StartsWith(@"\\")))
                {
                    return (filePath, fileSize);
                }
            }
            catch
            {
                // Parsing failed, return null
            }
        }
        
        return (null, 0);
    }
    
    /// <summary>
    /// Estimate progress by counting completed file operations in the log.
    /// This is approximate since robocopy doesn't provide real-time counters.
    /// </summary>
    public (long fileCount, long estimatedBytes) EstimateProgress(string logContent, long averageFileSize)
    {
        var lines = logContent.Split('\n');
        long fileCount = 0;
        long totalBytes = 0;
        
        foreach (var line in lines)
        {
            // With /V flag, robocopy outputs lines like:
            // "  New File  123456  filename.txt"
            // "  Newer     789012  document.pdf"
            
            if (line.Contains("New File") || 
                line.Contains("Newer") ||
                line.Contains("*EXTRA File") ||
                line.Contains("same"))
            {
                fileCount++;
                
                // Try to extract file size (appears after the status)
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    // Second element should be the size
                    if (long.TryParse(parts[1], out var size))
                    {
                        totalBytes += size;
                    }
                }
            }
        }
        
        // If we couldn't parse sizes, estimate
        if (totalBytes == 0 && fileCount > 0)
        {
            totalBytes = fileCount * averageFileSize;
        }
        
        return (fileCount, totalBytes);
    }
    
    /// <summary>
    /// Parse a number from string, handling formatting.
    /// </summary>
    private long ParseLong(string value)
    {
        // Remove any thousand separators or whitespace
        var cleaned = value.Replace(",", "").Replace(" ", "").Trim();
        
        if (long.TryParse(cleaned, out var result))
            return result;
        
        return 0;
    }
    
    /// <summary>
    /// Extract exit code interpretation.
    /// </summary>
    public (bool success, string message) InterpretExitCode(int exitCode)
    {
        bool success = exitCode < 8; // Exit codes 0-7 are considered success
        string message = RobocopyResult.InterpretExitCode(exitCode);
        
        return (success, message);
    }
}
