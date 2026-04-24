using System.Collections.Generic;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
///     Service for parsing .gitignore files into exclusion lists.
/// </summary>
public interface IGitIgnoreParserService
{
    /// <summary>
    ///     Parse a .gitignore file and return separate lists for directories and files.
    /// </summary>
    /// <param name="gitignoreFilePath">Path to the .gitignore file</param>
    /// <returns>Tuple containing (directories, files, unsupported patterns)</returns>
    (List<string> Directories, List<string> Files, List<string> UnsupportedPatterns) ParseGitIgnoreFile(
        string gitignoreFilePath);
}