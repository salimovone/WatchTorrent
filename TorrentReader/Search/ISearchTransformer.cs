using HtmlAgilityPack;
using TorrentReader.Search.Models;

namespace TorrentReader.Search
{
    internal interface ISearchTransformer
    {
        SearchResult Transform(HtmlDocument document, int page);
    }
}