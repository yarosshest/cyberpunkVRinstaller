using System.Text.Json.Serialization;

namespace CyberpunkVRInstaller;

internal sealed class InstallManifest
{
    public string InstallerVersion { get; set; } = InstallerEngine.ModVersion;
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;
    public List<InstalledFile> Files { get; set; } = [];
    public List<BackupItem> Backups { get; set; } = [];
    public bool IsDisabled { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public List<string> DisabledFiles { get; set; } = [];
}

internal sealed class InstalledFile
{
    public string RelativePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public bool ExistedBefore { get; set; }
}

internal sealed class BackupItem
{
    public string RelativePath { get; set; } = "";
    public string BackupRelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsObsoleteItem { get; set; }
}

internal sealed record DependencyStatus(string Name, string MinimumVersion, bool Found, string Details, bool Essential = true);

internal sealed record InstallProgress(int Percent, string Message);

internal sealed class InstallerException(string message) : Exception(message);

internal enum VrModState
{
    NotInstalled,
    Enabled,
    Disabled,
    Incomplete
}

internal sealed record ModStateResult(VrModState State, IReadOnlyList<string> Problems);

