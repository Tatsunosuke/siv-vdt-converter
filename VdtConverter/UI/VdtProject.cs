using System.Text.Json;
using System.Text.Json.Serialization;

namespace VdtConverter.UI;

/// <summary>
/// プロジェクトファイル (*.vdtproj、中身は JSON)。変換の設定をすべて保存して、あとで同じ状態に戻す。
/// 動画・出力・ポスター画像のパスは、絶対パスとプロジェクトファイルからの相対パスの両方を持ち、
/// フォルダごと移動しても相対パスで見つかるようにする。
/// </summary>
sealed class VdtProject
{
    public const string Extension = ".vdtproj";
    public const string Filter = "VDT Converter プロジェクト (*.vdtproj)|*.vdtproj";

    public int Version { get; set; } = 1;

    // ---- ファイル ----
    public string Input { get; set; } = "";
    public string InputRelative { get; set; } = "";
    public string Output { get; set; } = "";
    public string OutputRelative { get; set; } = "";

    // ---- 映像 ----
    public int TimeScale { get; set; } = 2;
    public bool Ntsc { get; set; }
    public bool Deinterlace { get; set; }
    public int ColorType { get; set; }
    public int Quality { get; set; } = 30;

    // ---- 縦横比 ----
    public int SourceAspect { get; set; }
    public int Fit { get; set; } = 1;

    // ---- 音声 ----
    public int RateCode { get; set; } = 4;
    public bool ShowExtendedRates { get; set; }
    public bool Normalize { get; set; } = true;
    public int VolumePercent { get; set; } = 100;

    // ---- 範囲・位置 (元動画のコマ番号) ----
    public int StartFrame { get; set; }
    /// <summary>-1 = 最後まで。</summary>
    public int EndFrame { get; set; } = -1;
    public int CursorFrame { get; set; }
    /// <summary>保存したときの元動画のフレームレート (別の動画に差し替わっていないかの目安)。</summary>
    public double SourceFps { get; set; }

    // ---- ポスター ----
    public bool PosterFromImage { get; set; }
    public int PosterFrame { get; set; }
    /// <summary>false = ポスターは開始コマについていく。</summary>
    public bool PosterTouched { get; set; }
    public string PosterImage { get; set; } = "";
    public string PosterImageRelative { get; set; } = "";

    // ---- その他 ----
    public string Comment { get; set; } = " Version 0.97a (VDT Converter)";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 日本語をそのまま書く
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public void Save(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        InputRelative = Relative(dir, Input);
        OutputRelative = Relative(dir, Output);
        PosterImageRelative = Relative(dir, PosterImage);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, true);
    }

    public static VdtProject Load(string path)
    {
        var p = JsonSerializer.Deserialize<VdtProject>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("プロジェクトファイルを読めませんでした。");
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        p.Input = Resolve(dir, p.Input, p.InputRelative, mustExist: true);
        p.Output = Resolve(dir, p.Output, p.OutputRelative, mustExist: false);
        p.PosterImage = Resolve(dir, p.PosterImage, p.PosterImageRelative, mustExist: true);
        return p;
    }

    static string Relative(string dir, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetRelativePath(dir, Path.GetFullPath(path)); } catch { return ""; }
    }

    /// <summary>絶対パスにあればそれ、無ければプロジェクトからの相対パスを試す。</summary>
    static string Resolve(string dir, string abs, string rel, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(abs) && string.IsNullOrWhiteSpace(rel)) return "";
        if (!string.IsNullOrWhiteSpace(abs) && (mustExist ? File.Exists(abs) : Directory.Exists(Path.GetDirectoryName(abs) ?? "")))
            return abs;
        if (!string.IsNullOrWhiteSpace(rel))
        {
            var r = Path.GetFullPath(Path.Combine(dir, rel));
            if (mustExist ? File.Exists(r) : Directory.Exists(Path.GetDirectoryName(r) ?? "")) return r;
        }
        return abs;   // 見つからなければ元のまま (あとで「見つかりません」と伝える)
    }
}
