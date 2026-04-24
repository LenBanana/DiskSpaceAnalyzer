using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace DiskSpaceAnalyzer.Services.Mft;

public static class MftElevationHelper
{
    public const string EngineArg = "--engine=mft";
    public const string PathArgPrefix = "--path=";

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsNtfsVolume(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path) ?? string.Empty;
            if (string.IsNullOrEmpty(root)) return false;
            return string.Equals(new DriveInfo(root).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // Relaunches the running exe with UAC elevation. Exits the current process
    // on success. On user refusal (returns false), the caller reverts its UI.
    public static bool TryRequestElevatedRestart(string? pathToRestore)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        var args = EngineArg;
        if (!string.IsNullOrWhiteSpace(pathToRestore))
            args += $" \"{PathArgPrefix}{pathToRestore}\"";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                // Preserve the launching CWD. Harmless for the usual Explorer/start-menu
                // case and avoids subtle breakage if anything downstream does relative-path
                // resolution (it currently doesn't, but cheap insurance).
                WorkingDirectory = Environment.CurrentDirectory
            };
            Process.Start(psi);
        }
        catch (Win32Exception)
        {
            // User cancelled the UAC dialog.
            return false;
        }

        Application.Current.Shutdown();
        return true;
    }

    public static (bool EngineRequested, string? Path) ParseLaunchArgs(string[] args)
    {
        var engine = false;
        string? path = null;
        foreach (var a in args)
            if (a.Equals(EngineArg, StringComparison.OrdinalIgnoreCase)) engine = true;
            else if (a.StartsWith(PathArgPrefix, StringComparison.OrdinalIgnoreCase))
                path = a.Substring(PathArgPrefix.Length).Trim('"');
        return (engine, path);
    }
}