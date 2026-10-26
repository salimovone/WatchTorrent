using Newtonsoft.Json;

namespace TorrentReader.Torrent.Models
{
    public class TorrentComment
    {
        public int CommentId { get; set; }

        public string Username { get; set; }

        public string Comment { get; set; }

        public string Posted { get; set; }

        public string Avatar { get; set; }

        public TorrentComment(int id, string username, string text, string datePostedOn, string avatarUrl)
        {
            CommentId = id;
            Username = username;
            Comment = text;
            Posted = datePostedOn;
            Avatar = avatarUrl;
        }

        public TorrentComment()
        {
        }
    }
}
