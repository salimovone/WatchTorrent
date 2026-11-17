using System.Text.Json;
using System.Text.Json.Serialization;
using WatchTorrent.Core;

namespace WatchTorrent.Libtorrent;

public sealed class LibtorrentEngine(ILibtorrentApi api) : ITorrentEngine, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string,TorrentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    public static Task<LibtorrentEngine> CreateAsync(string cache, bool mobile) => Task.Run(() =>
    {
        if(OperatingSystem.IsIOS()) RebaseIosManifest(cache);
        return new LibtorrentEngine(new NativeApi(cache, mobile));
    });
    private static void RebaseIosManifest(string cache)
    {
        var path=Path.Combine(cache,"transfers.json");
        if(!File.Exists(path))return;
        using var manifest=JsonDocument.Parse(File.ReadAllText(path));
        using var memory=new MemoryStream();
        using(var writer=new Utf8JsonWriter(memory))
        {
            writer.WriteStartArray();
            foreach(var entry in manifest.RootElement.EnumerateArray())
            {
                writer.WriteStartObject();
                foreach(var field in entry.EnumerateObject())
                {
                    if(field.Name=="Directory" && field.Value.GetString() is { } directory && directory.IndexOf("/Documents/",StringComparison.Ordinal) is var index && index>=0)
                        writer.WriteString("Directory",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),directory[(index+11)..]));
                    else field.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        File.WriteAllBytes(path+".tmp",memory.ToArray());
        File.Move(path+".tmp",path,true);
    }
    private async Task<string> ExecuteAsync(Command command, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            return await Task.Run(() => api.Request(JsonSerializer.Serialize(command, EngineJson.Default.Command)), token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public async Task<IReadOnlyList<TorrentSession>> ListAsync(CancellationToken token = default)
    {
        var json = await ExecuteAsync(new("list"), token).ConfigureAwait(false);
        return (JsonSerializer.Deserialize(json, EngineJson.Default.SnapshotArray) ?? []).Select(Apply).ToArray();
    }
    public async Task<TorrentSession> AddAsync(string magnet, string hash, string directory, CancellationToken token = default)
    {
        var parsed = Magnet.Parse(magnet);
        if (!parsed.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Magnet hash does not match.");
        var json = await ExecuteAsync(new("add", Magnet:magnet, Directory:directory), token).ConfigureAwait(false);
        return Apply(JsonSerializer.Deserialize(json, EngineJson.Default.Snapshot)!);
    }
    public async Task RefreshAsync(TorrentSession torrent, CancellationToken token = default)
    {
        var json = await ExecuteAsync(new("snapshot", torrent.Hash), token).ConfigureAwait(false);
        Apply(JsonSerializer.Deserialize(json, EngineJson.Default.Snapshot)!);
    }
    private TorrentSession Apply(Snapshot snapshot)
    {
        lock (_sessions)
        {
        if (!_sessions.TryGetValue(snapshot.Hash, out var session))
        {
            session = new Session(this) { Hash=snapshot.Hash, MagnetLink=new(snapshot.Magnet) };
            _sessions.Add(snapshot.Hash, session);
        }
        session.Name=snapshot.Name;
        session.MagnetLink=new(snapshot.Magnet,snapshot.Size);
        session.SavePath=snapshot.Directory;
        session.ContainingDirectory=snapshot.Directory;
        session.State=(TorrentState)snapshot.State;
        session.Error=string.IsNullOrEmpty(snapshot.Error) ? null : new(snapshot.Error);
        session.PartialProgress=Math.Clamp(snapshot.Progress,0,100);
        session.Monitor.DownloadRate=snapshot.DownloadRate;
        session.Monitor.UploadRate=snapshot.UploadRate;
        session.Peers.Seeds=snapshot.Seeds;
        session.Peers.Leeches=snapshot.Peers;
        if (session.Files.Count==0)
            foreach (var file in snapshot.Files)
            {
                var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(snapshot.Directory))+Path.DirectorySeparatorChar;
                var fullPath=Path.GetFullPath(Path.Combine(root,file.Path));
                if (!fullPath.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new IOException("Torrent file is outside its download directory.");
                session.Files.Add(new TorrentFile {Index=file.Index,Path=file.Path,FullPath=fullPath,Length=file.Length,
                    OffsetInTorrent=file.Offset,StartPieceIndex=file.Start,EndPieceIndex=file.End,Priority=(Priority)file.Priority});
            }
        session.Bitfield.Update(snapshot.Pieces);
        foreach (var file in session.Files)
        {
            file.Priority=(Priority)snapshot.Files.First(f=>f.Index==file.Index).Priority;
            file.BitField.Update(snapshot.Pieces.Skip(file.StartPieceIndex).Take(Math.Max(0,file.EndPieceIndex-file.StartPieceIndex+1)).ToArray());
        }
        if (snapshot.PieceLength > 0) session.Torrent=new(snapshot.Size,snapshot.PieceLength);
        return session;
        }
    }
    public async Task SetPausedAsync(TorrentSession torrent, bool paused, CancellationToken token = default)
    {
        await ExecuteAsync(new("pause",torrent.Hash,Paused:paused),token).ConfigureAwait(false);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        do
        {
            await RefreshAsync(torrent,timeout.Token).ConfigureAwait(false);
            if (torrent.State==TorrentState.Error) throw new IOException(torrent.Error?.Reason);
            if ((torrent.State is TorrentState.Stopped or TorrentState.Paused)==paused) return;
            await Task.Delay(100,timeout.Token).ConfigureAwait(false);
        } while (true);
    }
    public async Task RemoveAsync(TorrentSession torrent,bool deleteFiles,CancellationToken token=default)
    {
        await ExecuteAsync(new("remove",torrent.Hash,DeleteFiles:deleteFiles),token).ConfigureAwait(false);
        lock (_sessions) _sessions.Remove(torrent.Hash);
    }
    public Task<Stream> OpenStreamAsync(TorrentSession torrent,TorrentFile file,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new TorrentStream(this,torrent,file));
    }
    public async Task EndStreamingAsync(TorrentSession torrent,CancellationToken token=default) =>
        _=await ExecuteAsync(new("close_stream",torrent.Hash),token).ConfigureAwait(false);
    public async Task SaveAsync(CancellationToken token=default) => _=await ExecuteAsync(new("save"),token).ConfigureAwait(false);
    internal async Task<int> ReadAsync(TorrentSession torrent,TorrentFile file,long position,byte[] buffer,int offset,int count,CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            return await Task.Run(()=>api.Read(torrent.Hash,file.Index,position,buffer,offset,count),token).ConfigureAwait(false);
        }
        finally {_gate.Release();}
    }
    private sealed class Session(LibtorrentEngine engine):TorrentSession
    {
        public override async Task SetFilePriorityAsync(TorrentFile file,Priority priority)
        {
            await engine.ExecuteAsync(new("priority",Hash,Index:file.Index,Priority:(int)priority),default).ConfigureAwait(false);
            file.Priority=priority;
        }
    }
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if(_disposed)return;
            try { api.Request(JsonSerializer.Serialize(new Command("save"),EngineJson.Default.Command)); }
            finally { _disposed=true;api.Dispose(); }
        }
        finally {_gate.Release();}
    }
}

internal sealed record Command(string Op,string Hash="",string Magnet="",string Directory="",bool Paused=false,bool DeleteFiles=false,int Index=0,int Priority=1);
internal sealed record Snapshot(string Hash,string Name,string Magnet,string Directory,int State,string Error,double Progress,long Size,int PieceLength,long DownloadRate,long UploadRate,int Seeds,int Peers,FileSnapshot[] Files,bool[] Pieces);
internal sealed record FileSnapshot(int Index,string Path,long Length,long Offset,int Start,int End,int Priority);
[JsonSerializable(typeof(Command))]
[JsonSerializable(typeof(Snapshot))]
[JsonSerializable(typeof(Snapshot[]))]
internal partial class EngineJson:JsonSerializerContext;
