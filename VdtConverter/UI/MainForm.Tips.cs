namespace VdtConverter.UI;

/// <summary>画面のツールチップ。README を見なくても使えるように、各部品に説明を付ける。</summary>
public sealed partial class MainForm
{
    void SetupToolTips()
    {
        var t = _leftTips;
        t.AutoPopDelay = 20000;
        t.InitialDelay = 500;
        t.ReshowDelay = 100;
        t.ShowAlways = true;

        // ファイル
        Tip(_input, "変換する動画。MP4 / AVI / MPEG / MKV など、ffmpeg が読めるものなら何でも使えます。\nウインドウへのドラッグ＆ドロップでも指定できます。");
        Tip(_output, "書き出す VDT ファイル。X68000 で扱えるよう、名前は半角英数字 8 文字＋.VDT がおすすめです。");
        Tip(_ffmpeg, "動画の読み込みに使う ffmpeg.exe の場所。\nこのアプリと同じフォルダか PATH にあれば、自動で見つけます。");

        // 映像
        Tip(_fps, "1 秒あたりのコマ数。VDT のヘッダには TimeScale（= 60÷fps）として入ります。\n多いほど滑らかですが、X68000 側で使うメモリも増えます。");
        Tip(_deint, "ビデオから取り込んだ、横縞（インターレース）の出る素材で使います。\nPC で作った動画やアニメの配信素材では、ふつうは不要です。");
        Tip(_exact, "SiV 本来の速度で、実時間どおりに再生します。\n29.97fps の素材だと、約 1000 コマに 1 回、同じコマが重なります。");
        Tip(_ntsc, "テレビ録画や DVD などのビデオ素材向け。コマを重ねたり飛ばしたりせず 1:1 で取り込み、\n音声も同じ長さに合わせます。再生は 0.1% 速くなりますが、絵と音はずれません。");
        Tip(_colorType, "無圧縮：1 コマ 30,720 バイト。いちばんきれいで、展開も速い。\n色質 1〜3：SiV の JPEG 類似圧縮。数字が大きいほど色の情報を間引いて小さくなります（SiV の初期値は 3）。\n※ X68000 では読み込むときに展開するので、必要なメモリは無圧縮と同じです。");
        Tip(_quality, "圧縮の画質（1〜100）。SiV の初期値は 30 で、無圧縮の 1 割くらいの大きさになります。\n95 以上は SiV 側で一部のブロックが崩れることがあるので、90 くらいまでがおすすめです。");

        // 音声
        Tip(_normalize, "音量のいちばん大きいところが、ADPCM で出せる最大に届くように揃えます。\n小さい音の素材を聞きやすくしたいときに。");
        Tip(_volume, "音量の倍率（10〜400%）。大きくしすぎると音が割れます。");

        // 範囲
        Tip(_startF, "取り込みを始めるコマ（元動画のコマ番号、0 から）。\n右の画面で探して「ここを開始に [I]」を押しても決められます。");
        Tip(_endF, "取り込みを終えるコマ（このコマも含みます）。\n右の画面で探して「ここを終了に [O]」を押しても決められます。");
        Tip(_rangeAll, "動画の最初から最後までを範囲にします。");

        // 縦横比
        Tip(_aspAuto, "ffmpeg が報告する表示縦横比（SAR・回転の情報も考慮）を使います。");
        Tip(_asp43, "入力を 4:3 として扱います。トリムも黒帯もなしで、そのまま画面いっぱいに使います。");
        Tip(_fitCrop, "画面いっぱいに表示します。はみ出した左右（縦長の素材なら上下）を切ります。");
        Tip(_fitLetter, "絵を全部見せます。余った上下（縦長の素材なら左右）は黒帯になります。");
        Tip(_fitStretch, "絵を全部、画面いっぱいに押し込みます。そのぶん絵が歪みます。");

        // 動画を見ながら選ぶ
        Tip(_view, "X68000 での見え方（128×120、縦横比の収め方も反映）。\n← → で 1 コマ、Shift で 10 秒、Ctrl+Shift で 1 分。");
        Tip(_bar, "青：取り込む範囲　▼（オレンジ）：ポスター　赤線：いま見ているコマ。\nクリック・ドラッグでその位置へ移動します。");
        Tip(_setStart, "いま見ているコマを、取り込みの開始にします（キー：I）。");
        Tip(_setEnd, "いま見ているコマを、取り込みの終了にします（キー：O）。");
        Tip(_setPoster, "いま見ているコマを、ポスター（一覧に出る代表の絵）にします（キー：P）。");
        Tip(_goStart, "範囲の開始コマへ移動します（キー：Home）。");
        Tip(_goEnd, "範囲の終了コマへ移動します（キー：End）。");

        // ポスター
        Tip(_posterVideo, "動画のコマをポスターにします。下のスクロールバーで選べます。\n触らなければ開始コマになります。");
        Tip(_posterFile, "PNG / JPEG / BMP などの画像をポスターにします。\n128×120 に縮めて使います（収め方は映像と同じ設定）。");
        Tip(_posterPath, "ポスターにする画像ファイル。");
        Tip(_posterBrowse, "ポスターにする画像を選びます。");
        Tip(_posterReset, "ポスターを開始コマに戻します。");
        Tip(_posterView, "ポスターの X68000 での見え方。");

        // コメント・実行
        Tip(_comment, "VDT のヘッダに入るコメント（\"SiV\" の直後〜改行まで、最大 200 文字）。");
        Tip(_estimate, "変換後のファイルの大きさと、X68000 で展開に使うメモリの目安。\n30fps・15.6kHz だと 1 秒あたり約 0.92MB。12MB 機なら 12 秒前後が上限です。\nSIVPH.X・SIVP@H.X は HDD から読みながら再生するので、この上限はありません。");
        Tip(_run, "いまの設定で変換します。終わると「プレビュー」タブに結果が読み込まれます。");
        Tip(_cancel, "変換を途中でやめます。");
        Tip(_log, "変換の記録。うまくいかないときは、ここのメッセージを確認してください。");

        // 説明の行（ラベル）
        Tip(_rangeInfo, "選んだ範囲のコマ数・長さ・X68000 側で使うメモリの目安です。");
        Tip(_fitNote, "いまの縦横比と収め方で、画面にどう収まるかの説明です。");
        Tip(_cursorInfo, "右の画面で見ているコマの番号 / 元動画の全コマ数。");
        Tip(_posterInfo, "ポスターにしているコマの番号です。");

        // 項目名のラベルには、となりの入力欄と同じ説明を出す
        CopyTipsToLabels(this);
    }

    /// <summary>説明の付いていないラベルに、すぐ右（なければすぐ左）の部品の説明を写す。</summary>
    void CopyTipsToLabels(Control parent)
    {
        var list = parent.Controls.Cast<Control>().ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c is Label && string.IsNullOrEmpty(_leftTips.GetToolTip(c)))
            {
                string? tip = Neighbor(list, i + 1) ?? Neighbor(list, i - 1);
                if (tip != null) _leftTips.SetToolTip(c, tip);
            }
            if (c.HasChildren) CopyTipsToLabels(c);
        }
    }

    string? Neighbor(List<Control> list, int i)
    {
        if (i < 0 || i >= list.Count || list[i] is Label) return null;
        var t = _leftTips.GetToolTip(list[i]);
        return string.IsNullOrEmpty(t) ? null : t;
    }

    void Tip(Control c, string text) => _leftTips.SetToolTip(c, text);
}
