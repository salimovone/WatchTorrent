using WatchTorrent.Core;

namespace WatchTorrent.Libtorrent;

internal sealed class TorrentStream(LibtorrentEngine engine,TorrentSession torrent,TorrentFile file):Stream
{
    private readonly CancellationTokenSource _closed=new();
    private long _position;
    public override bool CanRead=>!_closed.IsCancellationRequested;
    public override bool CanSeek=>CanRead;
    public override bool CanWrite=>false;
    public override long Length=>file.Length;
    public override long Position {get=>Interlocked.Read(ref _position);set=>Seek(value,SeekOrigin.Begin);}
    public override long Seek(long offset,SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(!CanRead,this);
        long position=origin switch {SeekOrigin.Begin=>offset,SeekOrigin.Current=>Position+offset,SeekOrigin.End=>Length+offset,_=>throw new ArgumentOutOfRangeException(nameof(origin))};
        if(position<0||position>Length)throw new ArgumentOutOfRangeException(nameof(offset));
        Interlocked.Exchange(ref _position,position);return position;
    }
    public override int Read(byte[] buffer,int offset,int count)=>ReadAsync(buffer,offset,count,default).GetAwaiter().GetResult();
    public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if(offset<0||count<0||offset>buffer.Length-count)throw new ArgumentOutOfRangeException(nameof(count));
        ObjectDisposedException.ThrowIf(!CanRead,this);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,_closed.Token);
        var token=linked.Token;
        count=(int)Math.Min(count,Length-Position);
        if(count==0)return 0;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            var read=await engine.ReadAsync(torrent,file,Position,buffer,offset,count,token).ConfigureAwait(false);
            if(read>=0){Interlocked.Add(ref _position,read);return read;}
            await Task.Delay(100,token).ConfigureAwait(false);
        }
    }
    protected override void Dispose(bool disposing){if(disposing)_closed.Cancel();base.Dispose(disposing);}
    public override void Flush(){}
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
