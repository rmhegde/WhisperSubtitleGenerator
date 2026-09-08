using WhisperSubtitleGenerator.Core.Audio;
using WhisperSubtitleGenerator.Core.Models;
using WhisperSubtitleGenerator.Core.Subtitles;
using WhisperSubtitleGenerator.Core.Transcription;
using WhisperSubtitleGenerator.Core.Video;

namespace WhisperSubtitleGenerator.App;

public partial class MainForm : Form
{
    private readonly List<BatchItem> _items = new();
    private CancellationTokenSource? _cts;

    private static readonly string[] MediaExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv", ".m4v",
        ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aac", ".wma", ".opus"
    };

    public MainForm()
    {
        InitializeComponent();

        _modelBox.Items.AddRange(WhisperModelCatalog.All.ToArray());
        _modelBox.SelectedItem = WhisperModelCatalog.Default;

        // All 100 languages, auto-detect first. AutoComplete makes a list this long usable:
        // typing "kan" jumps to Kannada rather than scrolling.
        _languageBox.Items.AddRange(WhisperLanguages.All.ToArray());
        _languageBox.SelectedIndex = 0;

        UpdateUi();
        CheckFfmpeg();
    }

    /// <summary>
    /// ffmpeg is a hard requirement and its absence is the likeliest reason a first run fails.
    /// Say so in the window at startup rather than after the user has queued files and waited.
    /// </summary>
    private void CheckFfmpeg()
    {
        var found = FfmpegLocator.Locate();
        if (found.Found)
        {
            Log($"ffmpeg ready ({found.Source}): {found.Version}");
            Log($"{WhisperLanguages.Count} languages available. Models cache to {WhisperModelCatalog.CacheDirectory}");
            return;
        }

        Log("ffmpeg is required to read audio and video, and was not found.");
        _startButton.Enabled = false;

        // Offer to do it rather than telling the user to go away and read instructions. This is
        // the one dependency that stops a first run dead.
        var answer = MessageBox.Show(
            this,
            "ffmpeg is needed to read audio and video files, and it is not installed."
            + Environment.NewLine + Environment.NewLine +
            "Download it now? It is about 106 MB, goes into this app's own folder, and needs no "
            + "administrator rights - nothing else on your PC is changed."
            + Environment.NewLine + Environment.NewLine +
            "Choose No if you would rather install it yourself.",
            "One-time setup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer == DialogResult.Yes)
        {
            _ = InstallFfmpegAsync();
        }
        else
        {
            Log("Install it yourself with:  winget install Gyan.FFmpeg");
            Log("Then restart the app, or press 'Check again'.");
        }
    }

    private async Task InstallFfmpegAsync()
    {
        _installButton.Enabled = false;
        _startButton.Enabled = false;
        _progress.Style = ProgressBarStyle.Continuous;

        // Report only whole-percent changes; a callback per 80 KB block would put a thousand lines
        // in the log for one download.
        var progress = new Progress<InstallProgress>(p =>
        {
            Log(p.ToString());
            if (p.Percent >= 0) _progress.Value = Math.Min(100, p.Percent);
        });

        try
        {
            var path = await new FfmpegInstaller().InstallAsync(progress);
            Log($"ffmpeg installed to {path}");
            Log("Ready. Add files and press Generate subtitles.");
            _startButton.Enabled = _items.Count > 0;
            _installButton.Visible = false;
        }
        catch (Exception ex)
        {
            Log("Automatic install failed: " + ex.Message);
            Log("Install it yourself with:  winget install Gyan.FFmpeg");
            _installButton.Enabled = true;
        }
        finally
        {
            _progress.Value = 0;
        }
    }

    private async void OnInstallFfmpeg(object? sender, EventArgs e)
    {
        // Re-check first: the user may have installed it manually since startup, and a system
        // install that updated PATH is invisible to this already-running process - the locator
        // probes real directories precisely so that still works without a restart.
        var found = FfmpegLocator.Locate();
        if (found.Found)
        {
            Log($"ffmpeg found ({found.Source}): {found.Version}");
            _startButton.Enabled = _items.Count > 0;
            _installButton.Visible = false;
            return;
        }
        await InstallFfmpegAsync();
    }

    // ---- file queue -------------------------------------------------------------------------

    private void OnAddFiles(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Choose audio or video files",
            Filter = "Media files|" + string.Join(";", MediaExtensions.Select(x => "*" + x)) + "|All files|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
    }

    private void OnAddFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Add every media file in a folder" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var found = Directory.EnumerateFiles(dlg.SelectedPath, "*", SearchOption.AllDirectories)
                             .Where(IsMedia)
                             .ToArray();
        if (found.Length == 0)
        {
            MessageBox.Show(this, "No media files found in that folder.", "Nothing added",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        AddFiles(found);
    }

    private static bool IsMedia(string path) =>
        MediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void AddFiles(IEnumerable<string> paths)
    {
        int added = 0, duplicates = 0;
        foreach (var p in paths)
        {
            if (_items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase)))
            {
                duplicates++;
                continue;
            }
            var item = new BatchItem(p);
            _items.Add(item);
            _fileList.Items.Add(MakeRow(item));
            added++;
        }
        if (added > 0) Log($"Added {added} file(s).");
        if (duplicates > 0) Log($"Skipped {duplicates} already in the queue.");
        UpdateUi();
    }

    private static ListViewItem MakeRow(BatchItem item)
    {
        var row = new ListViewItem(item.FileName) { Tag = item };
        row.SubItems.Add(StateText(item.State));
        row.SubItems.Add("");
        return row;
    }

    private static string StateText(BatchItemState s) => s switch
    {
        BatchItemState.Pending   => "queued",
        BatchItemState.Running   => "working...",
        BatchItemState.Done      => "done",
        BatchItemState.Failed    => "FAILED",
        BatchItemState.Skipped   => "skipped",
        BatchItemState.Cancelled => "cancelled",
        _ => ""
    };

    private static Color StateColour(BatchItemState s) => s switch
    {
        BatchItemState.Done    => Color.FromArgb(0, 128, 0),
        BatchItemState.Failed  => Color.FromArgb(192, 0, 0),
        BatchItemState.Running => Color.FromArgb(0, 80, 200),
        BatchItemState.Skipped => Color.Gray,
        _ => SystemColors.WindowText
    };

    /// <summary>Refreshes one row in place, so a long batch updates without a full rebuild.</summary>
    private void RefreshRow(BatchItem item)
    {
        if (_fileList.InvokeRequired) { _fileList.BeginInvoke(() => RefreshRow(item)); return; }

        foreach (ListViewItem row in _fileList.Items)
        {
            if (!ReferenceEquals(row.Tag, item)) continue;
            row.SubItems[1].Text = StateText(item.State);
            row.SubItems[2].Text = item.Message ?? "";
            row.ForeColor = StateColour(item.State);
            row.EnsureVisible();
            break;
        }
    }

    private void OnRemoveSelected(object? sender, EventArgs e)
    {
        foreach (ListViewItem row in _fileList.SelectedItems.Cast<ListViewItem>().ToList())
        {
            if (row.Tag is BatchItem item) _items.Remove(item);
            _fileList.Items.Remove(row);
        }
        UpdateUi();
    }

    private void OnClearFiles(object? sender, EventArgs e)
    {
        _items.Clear();
        _fileList.Items.Clear();
        UpdateUi();
    }

    // Drag-and-drop: the fastest way to queue a folder of episodes.
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] dropped) return;

        var files = new List<string>();
        foreach (var p in dropped)
        {
            // Dropping a folder should queue what is inside it rather than being ignored.
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Where(IsMedia));
            else if (IsMedia(p))
                files.Add(p);
        }

        if (files.Count == 0) { Log("Nothing dropped that looks like media."); return; }
        AddFiles(files);
    }

    private void OnBurnToggled(object? sender, EventArgs e)
    {
        _burnModeBox.Enabled = _burnCheck.Checked;
        if (_burnCheck.Checked)
        {
            Log("Burn-in applies to VIDEO files only; audio files still get subtitle files.");
            // Burning needs an .srt to hand ffmpeg, so make sure one is produced.
            if (!_srtCheck.Checked) { _srtCheck.Checked = true; Log("Enabled .srt - burning needs one."); }
        }
    }

    private void OnChooseOutput(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Where should subtitle files go?" };
        if (dlg.ShowDialog(this) == DialogResult.OK) _outputBox.Text = dlg.SelectedPath;
    }

    // ---- run --------------------------------------------------------------------------------

    private async void OnStart(object? sender, EventArgs e)
    {
        if (_items.Count == 0) return;

        var formats = new List<SubtitleFormat>();
        if (_srtCheck.Checked) formats.Add(SubtitleFormat.Srt);
        if (_vttCheck.Checked) formats.Add(SubtitleFormat.Vtt);
        if (formats.Count == 0)
        {
            MessageBox.Show(this, "Pick at least one output format.", "Nothing to write",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var language = (WhisperLanguage)_languageBox.SelectedItem!;
        var options = new TranscriptionOptions
        {
            Model = ((ModelChoice)_modelBox.SelectedItem!).Type,
            Language = language.Code,
            TranslateToEnglish = _translateCheck.Checked,
            Formats = formats,
            OutputDirectory = string.IsNullOrWhiteSpace(_outputBox.Text) ? null : _outputBox.Text
        };

        // Re-queue anything from a previous run so Start means "run all of these" again.
        foreach (var item in _items.Where(i => i.State != BatchItemState.Pending))
        {
            item.Reset();
            RefreshRow(item);
        }

        _cts = new CancellationTokenSource();
        SetRunning(true);
        Log($"Starting {_items.Count} file(s) - {((ModelChoice)_modelBox.SelectedItem!).DisplayName} model, " +
            $"{language}{(_translateCheck.Checked ? ", translating to English" : "")}.");

        int completed = 0;
        var batch = new BatchProcessor();

        BurnOptions? burn = _burnCheck.Checked
            ? new BurnOptions
              {
                  Mode = _burnModeBox.SelectedIndex == 0 ? BurnMode.HardBurn : BurnMode.SoftMux
              }
            : null;

        var summary = await batch.RunAsync(
            _items,
            options,
            skipExisting: _skipExistingCheck.Checked,
            burn: burn,
            status: new Progress<string>(Log),
            onItemChanged: item =>
            {
                RefreshRow(item);
                if (item.State is BatchItemState.Done or BatchItemState.Failed or BatchItemState.Skipped)
                {
                    completed++;
                    BeginInvoke(() => _progress.Value = Math.Min(100, completed * 100 / Math.Max(1, _items.Count)));
                }
            },
            onSegment: s => Log($"    {s.Start:hh\\:mm\\:ss} {s.NormalizedText}"),
            ct: _cts.Token);

        Log($"Finished in {summary.Elapsed.TotalSeconds:F1}s - " +
            $"{summary.Done} done, {summary.Failed} failed, {summary.Skipped} skipped.");

        SetRunning(false);
        _cts?.Dispose();
        _cts = null;
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        Log("Cancelling after the current file...");
        _cancelButton.Enabled = false;
    }

    // ---- state ------------------------------------------------------------------------------

    private void SetRunning(bool running)
    {
        _startButton.Enabled  = !running && _items.Count > 0;
        _cancelButton.Enabled = running;
        _addButton.Enabled    = !running;
        _addFolderButton.Enabled = !running;
        _removeButton.Enabled = !running && _fileList.SelectedItems.Count > 0;
        _clearButton.Enabled  = !running && _items.Count > 0;
        _modelBox.Enabled     = !running;
        _languageBox.Enabled  = !running;
        _translateCheck.Enabled = !running;
        _skipExistingCheck.Enabled = !running;
        _burnCheck.Enabled    = !running;
        _installButton.Enabled = !running;
        _burnModeBox.Enabled  = !running && _burnCheck.Checked;
        _fileList.AllowDrop   = !running;
        if (!running) _progress.Value = 0;
    }

    private void UpdateUi()
    {
        _startButton.Enabled  = _items.Count > 0 && _cts is null;
        _clearButton.Enabled  = _items.Count > 0 && _cts is null;
        _removeButton.Enabled = _fileList.SelectedItems.Count > 0 && _cts is null;
        _fileCountLabel.Text  = _items.Count == 1 ? "1 file queued" : $"{_items.Count} files queued";
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => UpdateUi();

    private void Log(string message)
    {
        if (_logBox.InvokeRequired) { _logBox.BeginInvoke(() => Log(message)); return; }
        _logBox.AppendText(message + Environment.NewLine);
    }
}
