namespace WatchTorrent.Core;

public enum Priority { DoNotDownload = 0, Normal = 1, High = 6, Highest = 7 }
public enum TorrentState { Stopped, Paused, Error, Metadata, Starting, FetchingHashes, Hashing, HashingPaused, Downloading, Stopping, Seeding }
public sealed record TorrentMagnet(string Uri, long? Size = null)
{
    public string ToV1String() => Uri;
}
public sealed record TorrentMetadata(long Size, int PieceLength);
public sealed record TorrentError(string Reason, Exception? Exception = null);
public sealed class TransferMonitor
{
    public long DownloadRate { get; set; }
    public long UploadRate { get; set; }
}
public sealed class PeerCounts
{
    public int Seeds { get; set; }
    public int Leeches { get; set; }
    public int Leechs => Leeches;
}

/// <summary>Immutable snapshots can be read safely by the UI and VLC threads.</summary>
public sealed class PieceMap
{
    private bool[] _pieces = [];
    public void Update(bool[] pieces) => Volatile.Write(ref _pieces, pieces);
    public int Length => _pieces.Length;
    public int TrueCount => _pieces.Count(x => x);
    public bool AllTrue => Length > 0 && _pieces.All(x => x);
    public int FirstFalse(int first, int last)
    {
        var pieces = _pieces;
        if (first < 0 || first >= pieces.Length) return first;
        for (int i = first; i <= last; i++)
            if (i >= pieces.Length || !pieces[i]) return i;
        return -1;
    }
}

public sealed class TorrentFile
{
    public required int Index { get; init; }
    public required string Path { get; init; }
    public required string FullPath { get; init; }
    public long Length { get; init; }
    public long OffsetInTorrent { get; init; }
    public int StartPieceIndex { get; init; }
    public int EndPieceIndex { get; init; }
    public Priority Priority { get; set; }
    public PieceMap BitField { get; } = new();
}

public abstract class TorrentSession
{
    public required string Hash { get; init; }
    public string Name { get; set; } = "";
    public required TorrentMagnet MagnetLink { get; set; }
    public string SavePath { get; set; } = "";
    public string ContainingDirectory { get; set; } = "";
    public TorrentState State { get; set; }
    public TorrentError? Error { get; set; }
    public TorrentMetadata? Torrent { get; set; }
    public bool HasMetadata => Torrent != null && Files.Count > 0;
    public List<TorrentFile> Files { get; } = [];
    public PieceMap Bitfield { get; } = new();
    public TransferMonitor Monitor { get; } = new();
    public PeerCounts Peers { get; } = new();
    public int OpenConnections => Peers.Seeds + Peers.Leeches;
    public double PartialProgress { get; set; }
    public abstract Task SetFilePriorityAsync(TorrentFile file, Priority priority);
}

public interface ITorrentEngine
{
    Task<IReadOnlyList<TorrentSession>> ListAsync(CancellationToken token = default);
    Task<TorrentSession> AddAsync(string magnet, string hash, string directory, CancellationToken token = default);
    Task SetPausedAsync(TorrentSession torrent, bool paused, CancellationToken token = default);
    Task RemoveAsync(TorrentSession torrent, bool deleteFiles, CancellationToken token = default);
    Task RefreshAsync(TorrentSession torrent, CancellationToken token = default);
    Task<Stream> OpenStreamAsync(TorrentSession torrent, TorrentFile file, CancellationToken token = default);
}
