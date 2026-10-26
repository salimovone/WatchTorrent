using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;
using TorrentReader.Torrent.Models;

namespace TorrentReader.Torrent.Transformer
{
    internal class TorrentDetailTransformer : ITorrentDetailTransformer
    {
        public TorrentDetail Transform(HtmlDocument document)
        {
            var torrentDetailPageNode = document.DocumentNode.SelectSingleNode("//*[contains(@class, 'torrent-detail-page')]");

            var torrentNode = torrentDetailPageNode.SelectSingleNode(".//ul[1]");
            var title = torrentDetailPageNode.SelectSingleNode("div/h1").InnerText.Trim();
            var imageUrl = GetImageUrl(torrentDetailPageNode);
            var magnetDownload = torrentNode.SelectSingleNode("li[1]/a").GetAttributeValue("href", string.Empty);
            var torrentDownloadUrls = TransformTorrentDownloadUrls(torrentNode);

            var leftColumnDetailNode = torrentDetailPageNode.SelectSingleNode(".//ul[2]");
            var category = leftColumnDetailNode.SelectSingleNode("li[1]/span").InnerText;
            var type = leftColumnDetailNode.SelectSingleNode("li[2]/span").InnerText;
            var language = leftColumnDetailNode.SelectSingleNode("li[3]/span").InnerText;
            var totalSize = leftColumnDetailNode.SelectSingleNode("li[4]/span").InnerText;
            var uploadedBy = leftColumnDetailNode.SelectSingleNode("li[5]/span/a").InnerText;

            var rightColumnDetailNode = torrentDetailPageNode.SelectSingleNode(".//ul[3]");
            var amountDownloads = int.Parse(rightColumnDetailNode.SelectSingleNode("li[1]/span").InnerText);
            var dateLastChecked = rightColumnDetailNode.SelectSingleNode("li[2]/span").InnerText;
            var dateUploaded = rightColumnDetailNode.SelectSingleNode("li[3]/span").InnerText;
            var amountSeeders = int.Parse(rightColumnDetailNode.SelectSingleNode("li[4]/span").InnerText);
            var amountLeechers = int.Parse(rightColumnDetailNode.SelectSingleNode("li[5]/span").InnerText);

            var infoHash = torrentDetailPageNode.SelectSingleNode(".//*[contains(@class, 'infohash-box')]/p/span").InnerText;

            var torrentTabsNode = torrentDetailPageNode.SelectSingleNode(".//*[contains(@class, 'torrent-tabs')]");
            var tabPaneNodes = torrentTabsNode.SelectNodes(".//*[contains(@class, 'tab-content')]/div[contains(@class, 'tab-pane')]").ToList();
            var htmlDescription = tabPaneNodes[0].InnerHtml.Trim();
            var files = tabPaneNodes[1].SelectNodes("ul/li/text()").Select(x => x.InnerText).ToList();

            return new TorrentDetail(
                title,
                category,
                type,
                language,
                totalSize,
                uploadedBy,
                amountDownloads,
                dateLastChecked,
                dateUploaded,
                amountSeeders,
                amountLeechers,
                htmlDescription,
                infoHash,
                magnetDownload,
                imageUrl,
                torrentDownloadUrls,
                files);
        }

        private static string GetImageUrl(HtmlNode torrentDetailPageNode)
        {
            var imageNode = torrentDetailPageNode.SelectSingleNode(".//*[contains(@class, 'torrent-image')]/div/img");

            if(imageNode == null)
            {
                return string.Empty;
            }

            var imageUrl = imageNode.GetAttributeValue("src", string.Empty).ToString();

            if (imageUrl.StartsWith("//"))
            {
                return $"https://{imageUrl.Substring(2, imageUrl.Length - 2)}";
            }

            return imageUrl;
        }

        private static IReadOnlyList<TorrentDownload> TransformTorrentDownloadUrls(HtmlNode torrentNode)
        {
            return torrentNode.SelectNodes("li[contains(@class, 'dropdown')]/ul/li/a").Select(
                node =>
                {
                    var name = node.SelectSingleNode("text()").InnerText;
                    var mirrorUrl = node.GetAttributeValue("href", string.Empty);

                    return new TorrentDownload(name, mirrorUrl);
                })
                .Where(torrentDownload => !torrentDownload.Name.ToLower().Contains("magnet"))
                .ToList();
        }
    }
}
