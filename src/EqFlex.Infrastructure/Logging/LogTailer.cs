using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EqFlex.Infrastructure.Logging;

public sealed class LogTailer : IDisposable
{
    private const int BufferSize = 147456; // 144KB — same as EQLP
    private const int PollMs = 150;

    private readonly string _filePath;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action<string>? LineRead;
    public event Action<Exception>? Error;
    /// <summary>Fired when the file exceeds <see cref="MaxSizeBytes"/>. The tail loop exits
    /// after firing so the caller can archive and restart with a fresh file.</summary>
    public event Action? ArchiveNeeded;

    /// <summary>Archive threshold in bytes. 0 disables size checking.</summary>
    public long MaxSizeBytes { get; set; }

    public LogTailer(string filePath) => _filePath = filePath;

    public void Start(bool fromEnd = true)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _task = Task.Run(() => TailAsync(fromEnd, token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _task?.Wait(2000); } catch { }
        _cts?.Dispose();
        _cts = null;
        _task = null;
    }

    private async Task TailAsync(bool fromEnd, CancellationToken token)
    {
        try
        {
            using var fs = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (fromEnd)
                fs.Seek(0, SeekOrigin.End);

            using var reader = new StreamReader(fs);

            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is not null)
                {
                    LineRead?.Invoke(line);
                }
                else
                {
                    // Check archive threshold on every idle poll.
                    if (MaxSizeBytes > 0 && fs.Length >= MaxSizeBytes)
                    {
                        ArchiveNeeded?.Invoke();
                        return; // tail loop exits; caller restarts with fresh file
                    }
                    await Task.Delay(PollMs, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error?.Invoke(ex); }
    }

    // EQ log line format: [Day Mon DD HH:MM:SS YYYY]
    private static readonly Regex TimestampRx = new(
        @"^\[(?:Sun|Mon|Tue|Wed|Thu|Fri|Sat) (\w+) (\d{1,2}) (\d{2}):(\d{2}):(\d{2}) (\d{4})\] ",
        RegexOptions.Compiled);

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static int ParseMonth(string s) => s switch
    {
        "Jan" => 1, "Feb" => 2, "Mar" => 3, "Apr" => 4,
        "May" => 5, "Jun" => 6, "Jul" => 7, "Aug" => 8,
        "Sep" => 9, "Oct" => 10, "Nov" => 11, _ => 12
    };

    private static long ParseLineTimestamp(string line)
    {
        var m = TimestampRx.Match(line);
        if (!m.Success) return -1;
        try
        {
            var dt = new DateTime(
                int.Parse(m.Groups[6].Value),
                ParseMonth(m.Groups[1].Value),
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value),
                int.Parse(m.Groups[4].Value),
                int.Parse(m.Groups[5].Value),
                DateTimeKind.Unspecified);
            return (long)(dt - UnixEpoch).TotalSeconds;
        }
        catch { return -1; }
    }

    /// <summary>
    /// Replay lines from <paramref name="filePath"/>, optionally skipping entries older
    /// than <paramref name="cutoffTimestamp"/> (Unix seconds, 0 = no cutoff).
    /// Progress reports (linesYielded, bytesRead, totalBytes) so the progress bar reflects
    /// actual file position even while skipping old entries.
    /// </summary>
    public static async IAsyncEnumerable<string> ReplayAsync(
        string filePath,
        IProgress<(long linesRead, long bytesRead, long totalBytes)>? progress = null,
        long cutoffTimestamp = 0,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var totalBytes = new FileInfo(filePath).Length;
        long bytesRead = 0;
        long linesScanned = 0;
        long linesYielded = 0;

        using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var reader = new StreamReader(fs);
        string? line;
        while (!token.IsCancellationRequested && (line = await reader.ReadLineAsync(token)) is not null)
        {
            bytesRead += line.Length + 1; // +1 for newline
            linesScanned++;

            if (cutoffTimestamp > 0 && ParseLineTimestamp(line) < cutoffTimestamp)
            {
                // Progress still advances during skip phase so the bar moves smoothly.
                if (linesScanned % 2000 == 0)
                    progress?.Report((linesYielded, bytesRead, totalBytes));
                continue;
            }

            linesYielded++;
            if (linesScanned % 2000 == 0)
                progress?.Report((linesYielded, bytesRead, totalBytes));
            yield return line;
        }

        progress?.Report((linesYielded, bytesRead, totalBytes));
    }

    public void Dispose() => Stop();
}
