using System.Collections.Generic;

namespace DiskSpaceAnalyzer.Models.Robocopy;

/// <summary>
///     Represents a named preset for file/folder exclusions.
///     Can be built-in or user-created.
/// </summary>
public class ExclusionPreset
{
    /// <summary>Unique identifier for the preset.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name for the preset.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Folders to exclude (e.g., "node_modules", ".git").</summary>
    public List<string> ExcludedDirectories { get; set; } = [];

    /// <summary>Files to exclude (e.g., "*.tmp", "*.log").</summary>
    public List<string> ExcludedFiles { get; set; } = [];

    /// <summary>Whether this is a built-in preset (cannot be deleted).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Description of what this preset excludes.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Get built-in presets that ship with the application.
    /// </summary>
    public static List<ExclusionPreset> GetBuiltInPresets()
    {
        return
        [
            new()
            {
                Id = "none",
                Name = "None",
                Description = "No exclusions",
                IsBuiltIn = true,
                ExcludedDirectories = [],
                ExcludedFiles = []
            },

            new()
            {
                Id = "angular",
                Name = "Angular Project",
                Description = "Exclude node_modules, build outputs, and Angular cache",
                IsBuiltIn = true,
                ExcludedDirectories =
                [
                    "node_modules",
                    ".git",
                    ".angular",
                    "dist",
                    ".nx",
                    "coverage",
                    ".vscode"
                ],
                ExcludedFiles =
                [
                    "*.log",
                    ".DS_Store"
                ]
            },

            new()
            {
                Id = "nodejs",
                Name = "Node.js Project",
                Description = "Exclude node_modules and common build artifacts",
                IsBuiltIn = true,
                ExcludedDirectories =
                [
                    "node_modules",
                    ".git",
                    "dist",
                    "build",
                    "coverage",
                    ".vscode"
                ],
                ExcludedFiles =
                [
                    "*.log",
                    ".DS_Store"
                ]
            },

            new()
            {
                Id = "visualstudio",
                Name = "Visual Studio",
                Description = "Exclude build outputs and VS metadata",
                IsBuiltIn = true,
                ExcludedDirectories =
                [
                    "bin",
                    "obj",
                    ".vs",
                    "packages",
                    "TestResults",
                    ".git"
                ],
                ExcludedFiles =
                [
                    "*.user",
                    "*.suo"
                ]
            },

            new()
            {
                Id = "git",
                Name = "Git Only",
                Description = "Exclude only .git directory",
                IsBuiltIn = true,
                ExcludedDirectories = [".git"],
                ExcludedFiles = []
            },

            new()
            {
                Id = "build",
                Name = "Build Artifacts",
                Description = "Exclude common build output folders",
                IsBuiltIn = true,
                ExcludedDirectories =
                [
                    "bin",
                    "obj",
                    "dist",
                    "build",
                    "out",
                    "target",
                    "coverage"
                ],
                ExcludedFiles = []
            }
        ];
    }
}