using System.Security.Cryptography;
using System.Text.Json;

namespace CampusQuota;

internal sealed class Settings
{
    public List<Profile> Profiles { get; set; } = [];
    public Guid Selected { get; set; }
    public string? ApprovedCertificate { get; set; }
    internal static string? TestRoot { get; set; }
    public static string Root => TestRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CampusQuota");
    public static Settings Load()
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "settings.json");
        // Do not silently overwrite damaged settings or lose saved profile identifiers.
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Cannot read Campus Quota settings.") : new Settings();
        settings.EnsureProfile(); return settings;
    }
    public void EnsureProfile()
    {
        if (Profiles.Count == 0) Profiles.Add(new(Guid.NewGuid(), "My campus account"));
        if (!Profiles.Any(p => p.Id == Selected)) Selected = Profiles[0].Id;
    }
    public void Save() => WriteJson("settings.json", this);
    public static void WriteJson(string name, object value)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name); var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, true);
    }
}
internal static class CredentialStore
{
    private static string PathFor(Guid id) => Path.Combine(Settings.Root, "Credentials", id + ".bin");
    // DPAPI binds the ciphertext to this Windows user on this computer.
    public static void Save(Guid id, Credentials credentials)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathFor(id))!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            byte[] encrypted = ProtectedData.Protect(bytes, id.ToByteArray(), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(PathFor(id) + ".tmp", encrypted);
            File.Move(PathFor(id) + ".tmp", PathFor(id), true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static Credentials? Load(Guid id)
    {
        if (!File.Exists(PathFor(id))) return null;
        byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(PathFor(id)), id.ToByteArray(), DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<Credentials>(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static void Remove(Guid id) { if (File.Exists(PathFor(id))) File.Delete(PathFor(id)); }
}
