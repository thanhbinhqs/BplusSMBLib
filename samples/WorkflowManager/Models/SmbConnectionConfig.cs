namespace WorkflowManager.Models;

/// <summary>
/// Cấu hình kết nối SMB
/// </summary>
public sealed class SmbConnectionConfig
{
    public const string DefaultSharePath = @"\\192.168.1.250\share\image";
    public const string DefaultUsername = "share";
    public const string DefaultPassword = "1234567890";

    public required string SharePath { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }

    public static SmbConnectionConfig Default => new()
    {
        SharePath = DefaultSharePath,
        Username = DefaultUsername,
        Password = DefaultPassword
    };
}
