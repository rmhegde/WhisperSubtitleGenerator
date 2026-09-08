using WhisperSubtitleGenerator.Core.Subtitles;

namespace WhisperSubtitleGenerator.Core.Transcription;

public enum BatchItemState
{
    Pending,
    Running,
    Done,
    Failed,
    Skipped,
    Cancelled
}

/// <summary>One file in a batch, and what happened to it.</summary>
public sealed class BatchItem
{
    public BatchItem(string path) => Path = path;

    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public BatchItemState State { get; internal set; } = BatchItemState.Pending;
    public string? Message { get; internal set; }
    public int CueCount { get; internal set; }
    public TimeSpan Elapsed { get; internal set; }
    public IReadOnlyList<string> OutputPaths { get; internal set; } = Array.Empty<string>();

    /// <summary>
    /// Returns the item to Pending so a finished queue can be run again.
    /// <para>An explicit method rather than public setters: during a run the processor should be
    /// the only thing changing state, and public setters would invite a caller to edit an item
    /// mid-batch and desynchronise the summary counts.</para>
    /// </summary>
    public void Reset()
    {
        State = BatchItemState.Pending;
        Message = null;
        CueCount = 0;
        Elapsed = TimeSpan.Zero;
        OutputPaths = Array.Empty<string>();
    }
}

public sealed record BatchSummary(int Total, int Done, int Failed, int Skipped, TimeSpan Elapsed)
{
    public bool AllSucceeded => Failed == 0 && Done + Skipped == Total;
}

/// <summary>
/// Runs a queue of files through <see cref="SubtitleGenerator"/>.
///
/// <para>Files are processed SEQUENTIALLY on purpose. Whisper saturates the CPU on a single file
/// already, so running several at once makes the whole batch slower while multiplying peak memory -
/// the large model alone wants several GB. Sequential also keeps the log readable and makes
/// cancellation predictable.</para>
///
/// <para>A failure on one file never stops the queue. A batch of twenty should not die on file
/// three because it happens to have no audio track.</para>
/// </summary>
public sealed class BatchProcessor
{
    private readonly SubtitleGenerator _generator;

    public BatchProcessor(SubtitleGenerator? generator = null)
        => _generator = generator ?? new SubtitleGenerator();

    /// <summary>
    /// Processes every item in order.
    /// </summary>
    /// <param name="skipExisting">
    /// When true, a file whose subtitle output already exists is skipped rather than re-transcribed.
    /// Re-running a 200-file batch after adding one new file should not redo 200 transcriptions.
    /// </param>
    /// <param name="onItemChanged">Called whenever an item's state changes, for live UI updates.</param>
    public async Task<BatchSummary> RunAsync(
        IReadOnlyList<BatchItem> items,
        TranscriptionOptions options,
        bool skipExisting = false,
        IProgress<string>? status = null,
        Action<BatchItem>? onItemChanged = null,
        Action<SubtitleSegment>? onSegment = null,
        CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        int done = 0, failed = 0, skipped = 0;

        foreach (var item in items)
        {
            if (ct.IsCancellationRequested)
            {
                // Everything not yet started is reported as cancelled rather than left "Pending",
                // so the final list never implies work is still queued.
                if (item.State == BatchItemState.Pending)
                {
                    item.State = BatchItemState.Cancelled;
                    onItemChanged?.Invoke(item);
                }
                continue;
            }

            if (skipExisting && AllOutputsExist(item.Path, options))
            {
                item.State = BatchItemState.Skipped;
                item.Message = "subtitles already exist";
                skipped++;
                onItemChanged?.Invoke(item);
                status?.Report($"Skipped {item.FileName} - subtitles already exist.");
                continue;
            }

            item.State = BatchItemState.Running;
            item.Message = null;
            onItemChanged?.Invoke(item);

            try
            {
                var result = await _generator
                    .GenerateAsync(item.Path, options, status, onSegment, ct)
                    .ConfigureAwait(false);

                item.State = BatchItemState.Done;
                item.CueCount = result.Segments.Count;
                item.Elapsed = result.Elapsed;
                item.OutputPaths = result.OutputPaths;
                item.Message = $"{result.Segments.Count} cues in {result.Elapsed.TotalSeconds:F1}s";
                done++;
            }
            catch (OperationCanceledException)
            {
                item.State = BatchItemState.Cancelled;
                item.Message = "cancelled";
                onItemChanged?.Invoke(item);
                break;
            }
            catch (Exception ex)
            {
                item.State = BatchItemState.Failed;
                item.Message = ex.Message;
                failed++;
            }

            onItemChanged?.Invoke(item);
        }

        return new BatchSummary(items.Count, done, failed, skipped, DateTime.UtcNow - started);
    }

    /// <summary>True when every requested output format already exists for this input.</summary>
    internal static bool AllOutputsExist(string inputPath, TranscriptionOptions options)
    {
        var dir = options.OutputDirectory
                  ?? Path.GetDirectoryName(Path.GetFullPath(inputPath))
                  ?? Directory.GetCurrentDirectory();
        var baseName = Path.GetFileNameWithoutExtension(inputPath);

        return options.Formats.Count > 0
            && options.Formats.All(f =>
                   File.Exists(Path.Combine(dir, baseName + SubtitleWriter.ExtensionFor(f))));
    }
}
