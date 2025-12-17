namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Defines the method used for file integrity verification after copying.
/// Engine-agnostic - used by all copy engines that support integrity checks.
/// </summary>
public enum IntegrityCheckMethod
{
    /// <summary>No verification performed.</summary>
    None,
    
    /// <summary>
    /// Quick metadata check (file size and timestamp comparison).
    /// Fastest verification method with basic corruption detection.
    /// Speed: Instant (metadata only, no file content read)
    /// Use case: Quick sanity check, sufficient for most local copies
    /// </summary>
    Metadata,
    
    /// <summary>
    /// MD5 hash verification (legacy compatibility).
    /// Speed: ~300 MB/s on modern hardware
    /// Use case: Legacy systems, compatibility with existing checksums
    /// Note: Not cryptographically secure against intentional tampering
    /// </summary>
    MD5,
    
    /// <summary>
    /// SHA256 hash verification (cryptographic security).
    /// Speed: ~200 MB/s on modern hardware
    /// Use case: Secure verification, compliance requirements, important data
    /// Provides cryptographic guarantee against tampering
    /// </summary>
    SHA256,
    
    /// <summary>
    /// xxHash64 verification (ultra-fast, excellent corruption detection).
    /// Speed: ~2-5 GB/s on modern hardware
    /// Use case: Fast verification of large files, recommended for most use cases
    /// Excellent corruption detection but not cryptographically secure
    /// </summary>
    XXHash64,
    
    /// <summary>
    /// BLAKE3 hash verification (fast + cryptographic security, parallelizable).
    /// Speed: ~5-15 GB/s on modern multi-core hardware
    /// Use case: Best of both worlds - fast AND secure
    /// Recommended default for systems that support it
    /// Parallelizable across CPU cores for very large files
    /// </summary>
    Blake3
}
