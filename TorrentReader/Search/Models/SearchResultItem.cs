using System;

namespace TorrentReader.Search.Models
{
    public class SearchResultItem
    {
        public string Name { get; set; }

        public int AmountComments { get; set; }

        public int AmountSeeders { get; set; }

        public int AmountLeechers { get; set; }

        public DateTime UploadedOn { get; set; }

        public string Size { get; set; }

        public string UploaderName { get; set; }

        public int TorrentId { get; set; }

        public string Slug { get; set; }

        public SearchResultItem(
            string name,
            int amountComments,
            int amountSeeders,
            int amountLeechers,
            DateTime uploadedOn,
            string size,
            string uploaderName,
            int torrentId,
            string slug)
        {
            Name = name;
            AmountComments = amountComments;
            AmountSeeders = amountSeeders;
            AmountLeechers = amountLeechers;
            UploadedOn = uploadedOn;
            Size = size;
            UploaderName = uploaderName;
            TorrentId = torrentId;
            Slug = slug;
        }
    }
}
