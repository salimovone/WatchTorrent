using HtmlAgilityPack;

namespace TorrentReader.Torrent.Transformer
{
    internal interface ITorrentDetailTransformer
    {
        Models.TorrentDetail Transform(HtmlDocument document);
    }
}