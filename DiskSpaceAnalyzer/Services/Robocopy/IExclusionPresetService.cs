using System.Collections.Generic;
using DiskSpaceAnalyzer.Models.Robocopy;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
/// Service for managing exclusion presets.
/// </summary>
public interface IExclusionPresetService
{
    /// <summary>Get all available presets (built-in + user-created).</summary>
    List<ExclusionPreset> GetAllPresets();
    
    /// <summary>Get a specific preset by ID.</summary>
    ExclusionPreset? GetPresetById(string id);
    
    /// <summary>Save a new user preset.</summary>
    void SavePreset(ExclusionPreset preset);
    
    /// <summary>Delete a user preset (cannot delete built-in).</summary>
    bool DeletePreset(string id);
    
    /// <summary>Check if a preset name already exists.</summary>
    bool PresetExists(string name);
}
