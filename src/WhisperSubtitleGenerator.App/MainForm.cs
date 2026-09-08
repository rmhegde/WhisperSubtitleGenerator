using Whisper.net.Ggml;
using WhisperSubtitleGenerator.Core.Audio;
using WhisperSubtitleGenerator.Core.Models;
using WhisperSubtitleGenerator.Core.Subtitles;
using WhisperSubtitleGenerator.Core.Transcription;

namespace WhisperSubtitleGenerator.App;

public partial class MainForm : Form
{
    private readonly List<string> _files = new();
    private CancellationTokenSource? _cts;

    public MainForm()
    {
        InitializeComponent();
        _modelBox.Items.AddRange(WhisperModelCatalog.All.ToArray());
        _modelBox.SelectedItem = WhisperModelCatalog.Default;
        _languageBox.SelectedIndex = 0;
        UpdateButtons();
        CheckFfmpeg();
    }

    /// <summary>
    /// ffmpeg is a hard requirement and its absence is the single most likely reason a first run
    /// fails. Say so up front, in the window, rather than letting the user queue files and wait.
    /// </summary>
    private void CheckFfmpeg()
    {
        if (new AudioExtractor().IsAvailable(out var version))
        {
            Log($"ffmpeg found: {version}");
        }
        else
        {
            Log("ffmpeg NOT found on PATH. Install it (winget install Gyan.FFmpeg) and restart - " +
                "audio cannot be decoded without it.");
            _startButton.Enabled = false;
        }
    }

    private void OnAddFiles(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Choose audio or video files",
            Filter = "Media files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.aac|All files|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        foreach (var f in dlg.FileNames)
        {
            if (_files.Contains(f, StringComparer.OrdinalIgnoreCase)) continue;
            _files.Add(f);
            _fileList.Items.Add(Path.GetFileName(f));
        }
        UpdateButtons();
    }

    private void OnClearFiles(object? sender, EventArgs e)
    {
        _files.Clear();
        _fileList.Items.Clear();
        UpdateButtons();
    }

    private void OnChooseOutput(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Where should subtitle files go?" };
        if (dlg.ShowDialog(this) == DialogResult.OK) _outputBox.Text = dlg.SelectedPath;
    }

    private async void OnStart(object? sender, EventArgs e)
    {
        if (_files.Count == 0) return;

        var formats = new List<SubtitleFormat>();
        if (_srtCheck.Checked) formats.Add(SubtitleFormat.Srt);
        if (_vttCheck.Checked) formats.Add(SubtitleFormat.Vtt);
        if (formats.Count == 0)
        {
            MessageBox.Show(this, "Pick at least one output format.", "Nothing to write",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var options = new TranscriptionOptions
        {
            Model = ((ModelChoice)_modelBox.SelectedItem!).Type,
            Language = _languageBox.SelectedIndex == 0 ? "auto" : _languageBox.Text.Split(' ')[0],
            TranslateToEnglish = _translateCheck.Checked,
            Formats = formats,
            OutputDirectory = string.IsNullOrWhiteSpace(_outputBox.Text) ? null : _outputBox.Text
        };

        _cts = new CancellationTokenSource();
        SetRunning(true);

        var status = new Progress<string>(Log);
        var generator = new Core.Transcription.SubtitleGenerator();
        int done = 0, failed = 0;

        foreach (var file in _files.ToList())
        {
            if (_cts.IsCancellationRequested) break;
            try
            {
                Log($"--- {Path.GetFileName(file)} ---");
                var result = await generator.GenerateAsync(
                    file, options, status,
                    onSegment: s => BeginInvoke(() => Log($"  [{s.Start:hh\\:mm\\:ss}] {s.NormalizedText}")),
                    ct: _cts.Token);

                Log($"Done in {result.Elapsed.TotalSeconds:F1}s - {result.Segments.Count} cues -> " +
                    string.Join(", ", result.OutputPaths.Select(Path.GetFileName)));
                done++;
            }
            catch (OperationCanceledException)
            {
                Log("Cancelled.");
                break;
            }
            catch (Exception ex)
            {
                // One bad file must not stop the queue - a batch of 20 should not die on file 3.
                Log($"FAILED: {ex.Message}");
                failed++;
            }
            _progress.Value = Math.Min(100, (int)((done + failed) * 100.0 / _files.Count));
        }

        Log($"Finished. {done} succeeded, {failed} failed.");
        SetRunning(false);
        _cts?.Dispose();
        _cts = null;
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        Log("Cancelling after the current step...");
    }

    private void SetRunning(bool running)
    {
        _startButton.Enabled = !running && _files.Count > 0;
        _cancelButton.Enabled = running;
        _addButton.Enabled = !running;
        _clearButton.Enabled = !running && _files.Count > 0;
        _modelBox.Enabled = !running;
        _languageBox.Enabled = !running;
        if (!running) _progress.Value = 0;
    }

    private void UpdateButtons()
    {
        _startButton.Enabled = _files.Count > 0;
        _clearButton.Enabled = _files.Count > 0;
        _fileCountLabel.Text = _files.Count == 1 ? "1 file" : $"{_files.Count} files";
    }

    private void Log(string message)
    {
        if (_logBox.InvokeRequired) { _logBox.BeginInvoke(() => Log(message)); return; }
        _logBox.AppendText(message + Environment.NewLine);
    }
}
