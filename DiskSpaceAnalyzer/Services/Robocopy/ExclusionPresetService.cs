using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using DiskSpaceAnalyzer.Models.Robocopy;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
///     Manages exclusion presets with JSON persistence to AppData.
/// </summary>
public class ExclusionPresetService : IExclusionPresetService
{
    private readonly string _presetsFilePath;
    private List<ExclusionPreset> _userPresets = new();

    public ExclusionPresetService()
    {
        // Store user presets in AppData
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "DiskSpaceAnalyzer");

        // Ensure directory exists
        Directory.CreateDirectory(appFolder);

        _presetsFilePath = Path.Combine(appFolder, "exclusion-presets.json");

        // Load existing presets
        LoadUserPresets();
    }

    public List<ExclusionPreset> GetAllPresets()
    {
        var allPresets = new List<ExclusionPreset>();

        // Add built-in presets first
        allPresets.AddRange(ExclusionPreset.GetBuiltInPresets());

        // Add user presets
        allPresets.AddRange(_userPresets);

        return allPresets;
    }

    public ExclusionPreset? GetPresetById(string id)
    {
        return GetAllPresets().FirstOrDefault(p =>
            p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public void SavePreset(ExclusionPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name))
            throw new ArgumentException("Preset name cannot be empty.");

        // Generate ID if not set
        if (string.IsNullOrWhiteSpace(preset.Id)) preset.Id = "user_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        // Ensure it's marked as user preset
        preset.IsBuiltIn = false;

        // Remove existing preset with same ID (update scenario)
        _userPresets.RemoveAll(p => p.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase));

        // Add to user presets
        _userPresets.Add(preset);

        // Save to disk
        SaveUserPresets();
    }

    public bool DeletePreset(string id)
    {
        // Cannot delete built-in presets
        var preset = GetPresetById(id);
        if (preset == null || preset.IsBuiltIn)
            return false;

        // Remove from user presets
        var removed = _userPresets.RemoveAll(p =>
            p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;

        if (removed) SaveUserPresets();

        return removed;
    }

    public bool PresetExists(string name)
    {
        return GetAllPresets().Any(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadUserPresets()
    {
        try
        {
            if (File.Exists(_presetsFilePath))
            {
                var json = File.ReadAllText(_presetsFilePath);
                var presets = JsonSerializer.Deserialize<List<ExclusionPreset>>(json);

                if (presets != null) _userPresets = presets;
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash - just start with empty user presets
            Debug.WriteLine($"Failed to load user presets: {ex.Message}");
            _userPresets = new List<ExclusionPreset>();
        }
    }

    private void SaveUserPresets()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(_userPresets, options);
            File.WriteAllText(_presetsFilePath, json);
        }
        catch (Exception ex)
        {
            // Log error but don't crash
            Debug.WriteLine($"Failed to save user presets: {ex.Message}");
            throw new IOException($"Failed to save exclusion presets: {ex.Message}", ex);
        }
    }
}