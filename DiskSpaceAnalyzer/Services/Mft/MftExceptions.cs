using System;

namespace DiskSpaceAnalyzer.Services.Mft;

public enum MftUnavailableReason
{
    NotNtfs,
    NotElevated,
    UnsupportedPath,
    VolumeOpenFailed,
    FsControlFailed,
    InvalidMft
}

public sealed class MftUnavailableException : Exception
{
    public MftUnavailableException(MftUnavailableReason reason, string message, Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
    }

    public MftUnavailableReason Reason { get; }
}