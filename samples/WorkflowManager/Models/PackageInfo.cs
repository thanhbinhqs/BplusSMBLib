namespace WorkflowManager.Models;

/// <summary>
/// Thông tin về một package hợp lệ trên SMB share
/// </summary>
public sealed class PackageInfo
{
    public required string FolderName { get; init; }
    public required string BaseName { get; init; }
    public required string FullPath { get; init; }
    public required long TotalSize { get; init; }
    public required int FileCount { get; init; }
    public required bool HasHashFile { get; init; }
    public required bool HasWhdFile { get; init; }
    public required bool HasWclFile { get; init; }
    public required int WxxFileCount { get; init; }
    public List<string>? Files { get; init; }
    public bool IsValid => HasHashFile && HasWhdFile && HasWclFile && WxxFileCount >= 5 && WxxFileCount <= 12;
}

