using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
///     Parses .gitignore files and converts patterns to Robocopy-compatible exclusions.
/// </summary>
public class GitIgnoreParserService : IGitIgnoreParserService
{
    public (List<string> Directories, List<string> Files, List<string> UnsupportedPatterns) ParseGitIgnoreFile(
        string gitignoreFilePath)
    {
        var directories = new List<string>();
        var files = new List<string>();
        var unsupportedPatterns = new List<string>();

        if (!File.Exists(gitignoreFilePath))
            throw new FileNotFoundException($"The .gitignore file was not found: {gitignoreFilePath}");

        var lines = File.ReadAllLines(gitignoreFilePath);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            // Skip negation patterns (not supported in simple robocopy exclusions)
            if (line.StartsWith("!"))
            {
                unsupportedPatterns.Add(line);
                continue;
            }

            // Check for complex patterns that robocopy might not handle well
            if (IsComplexPattern(line))
            {
                unsupportedPatterns.Add(line);
                continue;
            }

            // Process the pattern
            var pattern = NormalizePattern(line);

            // Determine if it's a directory or file pattern
            if (IsDirectoryPattern(line))
            {
                // It's a directory pattern
                var dirPattern = pattern.TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(dirPattern)) directories.Add(dirPattern);
            }
            else
            {
                // It's a file pattern
                if (!string.IsNullOrWhiteSpace(pattern)) files.Add(pattern);
            }
        }

        return (directories, files, unsupportedPatterns);
    }

    private bool IsDirectoryPattern(string pattern)
    {
        // Patterns ending with / are explicitly directories
        if (pattern.EndsWith("/"))
            return true;

        // Patterns without extensions or wildcards are likely directories
        // Common directory patterns: node_modules, .git, bin, obj, etc.
        var cleanPattern = pattern.TrimStart('/').TrimEnd('/');

        // If it contains a wildcard for files (like *.log), it's a file pattern
        if (cleanPattern.Contains("*."))
            return false;

        // If it has no dot at all, it's a directory (e.g., node_modules, bin)
        if (!cleanPattern.Contains('.'))
            return true;

        // Get the last component of the path to check for file extensions
        var lastComponent = cleanPattern.Contains('/') || cleanPattern.Contains('\\')
            ? cleanPattern.Split(new[] { '/', '\\' }).Last()
            : cleanPattern;

        // Dot-directories like .git, .vscode, .angular (no extension after the dot)
        if (lastComponent.StartsWith(".") && lastComponent.IndexOf('.', 1) == -1)
            return true;

        // Common file extensions indicate it's a file
        var knownFileExtensions = new[]
            { ".log", ".lock", ".md", ".txt", ".json", ".xml", ".exe", ".dll", ".msi", ".db", ".suo", ".user" };
        if (knownFileExtensions.Any(ext => lastComponent.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        // If the last component has an extension (dot not at start), likely a file
        var lastDotIndex = lastComponent.LastIndexOf('.');
        if (lastDotIndex > 0 && lastDotIndex < lastComponent.Length - 1)
            return false; // Has extension like .Store, .db, .gitignore

        // Default to directory for ambiguous cases
        return true;
    }

    private string NormalizePattern(string pattern)
    {
        // Remove leading slash (gitignore uses / for root, robocopy doesn't need it)
        pattern = pattern.TrimStart('/');

        // Handle trailing wildcards: .vscode/* -> .vscode (robocopy can't handle path\*)
        if (pattern.EndsWith("/*")) pattern = pattern.Substring(0, pattern.Length - 2);

        // Remove trailing slash for consistency
        pattern = pattern.TrimEnd('/');

        // Convert forward slashes to backslashes for Windows/Robocopy
        pattern = pattern.Replace('/', '\\');

        return pattern;
    }

    private bool IsComplexPattern(string pattern)
    {
        // Detect patterns that are too complex for simple robocopy exclusions

        // Negation patterns
        if (pattern.StartsWith("!"))
            return true;

        // Character classes like [abc] or [!abc]
        if (pattern.Contains("[") && pattern.Contains("]"))
            return true;

        // Double-star patterns in the middle (e.g., **/test/** is complex)
        // Single ** at start or end is OK, but complex path matching isn't
        if (pattern.Contains("**/") && pattern.IndexOf("**/") != 0)
            return true;

        // Path-specific patterns (e.g., /path/to/file)
        // These need special handling that robocopy doesn't support well
        if (pattern.StartsWith("/") && pattern.Count(c => c == '/') > 2)
            return true;

        // Robocopy doesn't support wildcards inside paths (e.g., path\* or path\sub*\file)
        // Only supports wildcards in the final component
        var normalized = pattern.TrimStart('/').Replace('/', '\\');
        if (normalized.Contains("\\") && normalized.Contains("*"))
        {
            // Check if wildcard is in the middle of a path
            var lastBackslash = normalized.LastIndexOf('\\');
            var firstWildcard = normalized.IndexOf('*');
            if (firstWildcard < lastBackslash)
                return true; // Wildcard before last path component
        }

        return false;
    }
}