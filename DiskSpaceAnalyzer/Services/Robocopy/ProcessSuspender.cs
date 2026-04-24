using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
///     Utility for suspending and resuming Windows processes via thread manipulation.
///     Uses P/Invoke to call kernel32.dll functions.
/// </summary>
public class ProcessSuspender
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    ///     Suspend all threads of a process.
    /// </summary>
    /// <param name="process">The process to suspend.</param>
    /// <returns>Number of threads suspended.</returns>
    public int SuspendProcess(Process process)
    {
        if (process.HasExited)
            return 0;

        var suspendedCount = 0;

        try
        {
            // Refresh to get current threads
            process.Refresh();

            foreach (ProcessThread thread in process.Threads)
            {
                var threadHandle = IntPtr.Zero;

                try
                {
                    threadHandle = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);

                    if (threadHandle == IntPtr.Zero)
                        continue;

                    var result = SuspendThread(threadHandle);

                    // SuspendThread returns the previous suspend count
                    // If it's not 0xFFFFFFFF (error), we successfully suspended
                    if (result != 0xFFFFFFFF)
                        suspendedCount++;
                }
                finally
                {
                    if (threadHandle != IntPtr.Zero)
                        CloseHandle(threadHandle);
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - partial suspend is better than failure
            Debug.WriteLine($"Error suspending process: {ex.Message}");
        }

        return suspendedCount;
    }

    /// <summary>
    ///     Resume all threads of a process.
    /// </summary>
    /// <param name="process">The process to resume.</param>
    /// <returns>Number of threads resumed.</returns>
    public int ResumeProcess(Process process)
    {
        if (process.HasExited)
            return 0;

        var resumedCount = 0;

        try
        {
            // Refresh to get current threads
            process.Refresh();

            foreach (ProcessThread thread in process.Threads)
            {
                var threadHandle = IntPtr.Zero;

                try
                {
                    threadHandle = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);

                    if (threadHandle == IntPtr.Zero)
                        continue;

                    int result;

                    // Resume thread repeatedly until suspend count reaches 0
                    // (in case it was suspended multiple times)
                    do
                    {
                        result = ResumeThread(threadHandle);
                        if (result > 0)
                            resumedCount++;
                    } while (result > 0);
                }
                finally
                {
                    if (threadHandle != IntPtr.Zero)
                        CloseHandle(threadHandle);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error resuming process: {ex.Message}");
        }

        return resumedCount;
    }

    /// <summary>
    ///     Check if a process is currently suspended.
    ///     Note: This is a best-effort check and may not be 100% accurate.
    /// </summary>
    public bool IsProcessSuspended(Process process)
    {
        if (process.HasExited)
            return false;

        try
        {
            process.Refresh();

            // Check if any thread is running
            foreach (ProcessThread thread in process.Threads)
                if (thread.ThreadState == ThreadState.Running)
                    return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    [Flags]
    private enum ThreadAccess
    {
        SUSPEND_RESUME = 0x0002,
        TERMINATE = 0x0001,
        GET_CONTEXT = 0x0008,
        SET_CONTEXT = 0x0010,
        QUERY_INFORMATION = 0x0040
    }
}