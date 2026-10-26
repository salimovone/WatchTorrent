using System.Collections.Generic;

namespace TorrentReader.Torrent.Models
{
    public class TorrentDetail
    {
        public string Title { get; set; }

        public string Category { get; set; }

        public string Type { get; set; }

        public string Language { get; set; }

        public string TotalSize { get; set; }

        public string UploadedBy { get; set; }

        public int AmountDownloads { get; set; }

        public string DateLastChecked { get; set; }

        public string DateUploaded { get; set; }

        public int AmountSeeders { get; set; }

        public int AmountLeechers { get; set; }

        public string HtmlDescription { get; set; }

        public string InfoHash { get; set; }

        public string MagnetDownload { get; set; }

        public string ImageUrl { get; set; }

        public IReadOnlyList<TorrentDownload> TorrentDownloadUrls { get; set; }

        public IReadOnlyList<string> Files { get; set; }

        public TorrentDetail(
            string title,
            string category,
            string type,
            string language,
            string totalSize,
            string uploadedBy,
            int amountDownloads,
            string dateLastChecked,
            string dateUploaded,
            int amountSeeders,
            int amountLeechers,
            string htmlDescription,
            string infoHash,
            string magnetDownload,
            string imageUrl,
            IReadOnlyList<TorrentDownload> torrentDownloadUrls,
            IReadOnlyList<string> files)
        {
            Title = title;
            Category = category;
            Type = type;
            Language = language;
            TotalSize = totalSize;
            UploadedBy = uploadedBy;
            AmountDownloads = amountDownloads;
            DateLastChecked = dateLastChecked;
            DateUploaded = dateUploaded;
            AmountSeeders = amountSeeders;
            AmountLeechers = amountLeechers;
            HtmlDescription = htmlDescription;
            InfoHash = infoHash;
            MagnetDownload = magnetDownload;
            ImageUrl = imageUrl;
            TorrentDownloadUrls = torrentDownloadUrls;
            Files = files;
        }
    }
}
