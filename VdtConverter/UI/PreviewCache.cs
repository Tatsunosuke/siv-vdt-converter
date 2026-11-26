using VdtConverter.Core;

namespace VdtConverter.UI;

/// <summary>
/// コマ送りプレビュー用のキャッシュ。変換後の 128x120 (X68000 形式) のコマを、コマ番号で覚えておく。
/// 足りないコマは ffmpeg を 1 回起動して前後をまとめて (Window コマ) 取ってくるので、
/// ◀ ▶ やキーでのコマ送りは 2 回目以降ほぼ一瞬になる。
/// 収め方・縦横比・インターレース解除が変わったら Reset() で捨てる。
/// </summary>
sealed class PreviewCache
{
    const int Window = 120;      // 1 回で読むコマ数 (約 4 秒)
    const int Behind = 30;       // そのうち手前に取るコマ数 (◀ でも戻れるように)
    const int Capacity = 3000;   // 覚えておく最大コマ数 (30KB × 3000 ≒ 90MB)

    readonly Dictionary<int, byte[]> _frames = new();
    readonly LinkedList<int> _lru = new();
    readonly Dictionary<int, LinkedListNode<int>> _nodes = new();
    readonly List<(int From, int To, Task Task)> _loading = new();
    string _key = "";
    int _generation;

    public void Reset()
    {
        _frames.Clear(); _lru.Clear(); _nodes.Clear(); _loading.Clear();
        _generation++;
    }

    /// <summary>設定が前回と違えば中身を捨てる。</summary>
    public void Use(string key)
    {
        if (key != _key) { _key = key; Reset(); }
    }

    public bool TryGet(int frame, out byte[] img)
    {
        if (_frames.TryGetValue(frame, out img!))
        {
            Touch(frame);
            return true;
        }
        return false;
    }

    public bool Has(int frame) => _frames.ContainsKey(frame);

    /// <summary>
    /// frame を含むまとまりを読み込む (読み込み中ならそれを待つ)。読めたら true。
    /// UI スレッドから呼ぶこと (中身の更新は await の後に UI スレッドで行う)。
    /// </summary>
    public async Task<bool> Load(int frame, int lastFrame, ConvertOptions o, MediaInfo info, CancellationToken ct, bool forward = true)
    {
        if (Has(frame)) return true;
        foreach (var l in _loading)
            if (frame >= l.From && frame <= l.To) { await l.Task; return Has(frame); }

        int from = forward ? Math.Max(0, frame - Behind) : Math.Max(0, frame - (Window - Behind));
        // 既に持っているコマはなるべく読み直さない
        while (from < frame && Has(from)) from++;
        int to = Math.Min(lastFrame, from + Window - 1);
        int gen = _generation;
        var task = Fetch(from, to - from + 1, o, info, gen, ct);
        var entry = (from, to, (Task)task);
        _loading.Add(entry);
        try { await task; }
        catch (OperationCanceledException) { }
        finally { _loading.Remove(entry); }
        return Has(frame);
    }

    async Task Fetch(int from, int count, ConvertOptions o, MediaInfo info, int gen, CancellationToken ct)
    {
        var ff = new Ffmpeg(o.FfmpegPath);
        var rgbs = await ff.ReadFrameRun(o.Input, info.FrameToSeconds(from), count, o.VideoFit, o.Deinterlace, o.Aspect, ct);
        var imgs = await Task.Run(() => rgbs.Select(r => X68Color.FrameFromRgb24(r)).ToList(), ct);
        if (gen != _generation) return;   // 途中で設定が変わった
        for (int i = 0; i < imgs.Count; i++) Put(from + i, imgs[i]);
    }

    void Put(int frame, byte[] img)
    {
        _frames[frame] = img;
        Touch(frame);
        while (_frames.Count > Capacity && _lru.First is { } old)
        {
            _lru.RemoveFirst();
            _nodes.Remove(old.Value);
            _frames.Remove(old.Value);
        }
    }

    void Touch(int frame)
    {
        if (_nodes.TryGetValue(frame, out var n)) _lru.Remove(n);
        _nodes[frame] = _lru.AddLast(frame);
    }
}
