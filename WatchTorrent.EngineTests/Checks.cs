using System.Text.Json;
using WatchTorrent.Core;
using WatchTorrent.Libtorrent;
using WatchTorrent.Services;

var checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
Check(Magnet.Parse("magnet:?xt=urn:btih:"+new string('A',40)).Hash==new string('a',40),"v1 hex magnet normalization");
Check(Magnet.Parse("magnet:?xt=urn:btih:"+new string('A',32)).Hash==new string('0',40),"base32 magnet identity");
Check(Magnet.Parse("magnet:?xt=urn:btmh:1220"+new string('b',64)).Hash.Length==64,"v2 magnet identity");
Check(!Magnet.TryParse("magnet:?xt=urn:btih:bad",out _),"invalid magnet rejected");
Check(!Magnet.TryParse("https://example.com/",out _),"non-magnet rejected");
var fake=new FakeNative();
using var engine=new LibtorrentEngine(fake);
var torrent=await engine.AddAsync("magnet:?xt=urn:btih:"+fake.Hash,fake.Hash,Path.GetTempPath());
Check(torrent.Files.Count==1&&torrent.Torrent?.PieceLength==4,"native metadata maps to engine-neutral files");
Check(torrent.Bitfield.FirstFalse(0,1)==0,"pending pieces are not verified");
var item=new DownloadItem(torrent,null);
Check(item.TryBeginOperation("Pauza qilinmoqda…")&&!item.CanInteract&&item.IsBusy,"UI responds immediately and disables repeat actions");
Check(!item.TryBeginOperation("again"),"duplicate operation rejected");
item.Refresh();
Check(item.StatusText=="Pauza qilinmoqda…","poll does not overwrite pending status");
item.EndOperation();
Check(item.CanInteract&&!item.IsBusy,"controls recover");
await engine.SetPausedAsync(torrent,true);
Check(torrent.State==TorrentState.Stopped,"pause confirmed by native state");
await engine.SetPausedAsync(torrent,false);
Check(torrent.State==TorrentState.Downloading,"resume confirmed by native state");
await torrent.SetFilePriorityAsync(torrent.Files[0],Priority.Highest);
Check(fake.LastPriority==7,"file priorities reach libtorrent");
using var stream=await engine.OpenStreamAsync(torrent,torrent.Files[0]);
var buffer=new byte[4];
using(var cancellation=new CancellationTokenSource(100))
{
    try{_=await stream.ReadAsync(buffer,0,4,cancellation.Token);throw new Exception("Unverified bytes exposed");}
    catch(OperationCanceledException){Check(true,"pending native reads cancel");}
}
fake.Ready=true;
Check(await stream.ReadAsync(buffer,0,4)==4&&buffer[0]==1,"verified piece bytes reach the player");
stream.Seek(4,SeekOrigin.Begin);
Check(await stream.ReadAsync(buffer,0,4)==4&&buffer[0]==5,"seek reads from new position");
Check(await stream.ReadAsync(buffer,0,4)==0,"EOF returns zero");
await engine.EndStreamingAsync(torrent);
Check(fake.Closed,"stream deadlines are cleared");
await engine.SaveAsync();
Check(fake.Saved,"resume data save requested");
await engine.RemoveAsync(torrent,false);
Check(!fake.DeletedFiles,"remove can retain downloaded files");

if(args.Contains("--native"))
{
    var directory=Path.Combine(Path.GetTempPath(),"watchtorrent-native-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        using(var real=await LibtorrentEngine.CreateAsync(directory,false))
        {
            Check((await real.ListAsync()).Count==0,"real native session starts");
            var added=await real.AddAsync("magnet:?xt=urn:btih:"+fake.Hash,fake.Hash,directory);
            await real.SetPausedAsync(added,true);
            await real.SaveAsync();
        }
        using(var restored=await LibtorrentEngine.CreateAsync(directory,false))
        {
            var list=await restored.ListAsync();
            Check(list.Count==1&&list[0].State==TorrentState.Stopped,"real native magnet and pause state persist");
            await restored.RemoveAsync(list[0],false);
        }
    }
    finally
    {
        foreach(var path in Directory.EnumerateFiles(directory))File.Delete(path);
        Directory.Delete(directory);
    }
}
Console.WriteLine($"{checks} checks passed.");

sealed class FakeNative:ILibtorrentApi
{
    public string Hash=>new('a',40);
    public bool Ready,Closed,Saved,DeletedFiles;
    public int LastPriority=1;
    bool _paused;
    public string Request(string command)
    {
        using var json=JsonDocument.Parse(command);
        var c=json.RootElement;
        switch(c.GetProperty("Op").GetString())
        {
            case "add":case "snapshot":return Snapshot();
            case "list":return "["+Snapshot()+"]";
            case "pause":_paused=c.GetProperty("Paused").GetBoolean();break;
            case "priority":LastPriority=c.GetProperty("Priority").GetInt32();break;
            case "close_stream":Closed=true;break;
            case "save":Saved=true;break;
            case "remove":DeletedFiles=c.GetProperty("DeleteFiles").GetBoolean();break;
            default:throw new Exception("Unexpected native command");
        }
        return "null";
    }
    string Snapshot()=>JsonSerializer.Serialize(new {Hash,Name="test",Magnet="magnet:?xt=urn:btih:"+Hash,Directory=Path.GetTempPath(),State=_paused?0:8,Error="",Progress=Ready?100:0,Size=8,PieceLength=4,DownloadRate=100,UploadRate=0,Seeds=1,Peers=0,
        Files=new[]{new{Index=0,Path="video.mp4",Length=8,Offset=0,Start=0,End=1,Priority=LastPriority}},Pieces=new[]{Ready,Ready}});
    public int Read(string hash,int file,long position,byte[] buffer,int offset,int count)
    {
        if(!Ready)return -1;
        int n=(int)Math.Min(count,8-position);
        for(int i=0;i<n;i++)buffer[offset+i]=(byte)(position+i+1);
        return n;
    }
    public void Dispose(){}
}
