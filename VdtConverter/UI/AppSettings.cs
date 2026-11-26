using System.Text.Json;

namespace VdtConverter.UI;

/// <summary>前回の設定を %AppData%\VdtConverter\settings.json に保存する。</summary>
sealed class AppSettings
{
    public string FfmpegPath { get; set; } = "";
    public string LastInputDir { get; set; } = "";
    public string LastOutputDir { get; set; } = "";
    public int TimeScale { get; set; } = 2;
    public bool Ntsc { get; set; }
    public int Fit { get; set; } = (int)Core.FitMode.Letterbox;
    public int SourceAspect { get; set; }
    public bool Deinterlace { get; set; }
    public int ColorType { get; set; }
    public int Quality { get; set; } = 30;
    public string LastProjectDir { get; set; } = "";
    public List<string> RecentProjects { get; set; } = new();
    public int RateCode { get; set; } = 4;
    public bool ShowExtendedRates { get; set; }
    public bool Normalize { get; set; } = true;
    public int VolumePercent { get; set; } = 100;
    public string Comment { get; set; } = " Version 0.97a (VDT Converter)";

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VdtConverter", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
