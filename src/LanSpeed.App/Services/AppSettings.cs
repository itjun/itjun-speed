using System.Text.Json;

namespace LanSpeed.App.Services;

/// <summary>应用设置（%LOCALAPPDATA%\LanSpeed\settings.json）。</summary>
public sealed class AppSettings
{
    public bool AllowBeingTested { get; set; } = true;

    public int CtrlPort { get; set; } = 39301;

    public bool AutoStart { get; set; }

    /// <summary>检查更新用的版本信息 URL；为空表示不检查（M4 简版：读取 JSON {"version":"x.y.z"}）。</summary>
    public string UpdateCheckUrl { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSpeed", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (IOException)
        {
        }
    }
}
