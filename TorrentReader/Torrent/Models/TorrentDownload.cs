namespace TorrentReader.Torrent.Models
{
    public class TorrentDownload
    {
        public string Name { get; set; }

        public string MirrorUrl { get; set; }

        public TorrentDownload(string name, string mirrorUrl)
        {
            Name = name;
            MirrorUrl = mirrorUrl;
        }
    }
}
