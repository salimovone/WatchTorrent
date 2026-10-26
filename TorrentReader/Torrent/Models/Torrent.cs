using System.Collections.Generic;

namespace TorrentReader.Torrent.Models
{
    public class Torrent
    {
        public TorrentDetail Detail { get; set; }

        public IReadOnlyList<TorrentComment> Comments { get; set; }

        public Torrent(TorrentDetail detail, IReadOnlyList<TorrentComment> comments)
        {
            Detail = detail;
            Comments = comments;
        }
    }
}
