using System;
using System.Linq;
using HtmlAgilityPack;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Search
{
    internal class SearchTransformer : ISearchTransformer
    {
        private readonly IOverviewTransformer _overviewTransformer;

        public SearchTransformer(IOverviewTransformer overviewTransformer)
        {
            _overviewTransformer = overviewTransformer;
        }

        public SearchResult Transform(HtmlDocument document, int page)
        {
            var searchResultItems = _overviewTransformer.Transform(document);

            if(!searchResultItems.Any())
            {
                return new SearchResult(searchResultItems, 0, 0);
            }

            var paginationLastPageNode = document.DocumentNode.SelectSingleNode("//*[contains(@class, 'pagination')]/ul/li[last()]/a[1]");

            int amountPages = 1;

            if(paginationLastPageNode != null)
            {
                var lastPageRelativeUrl = paginationLastPageNode.GetAttributeValue("href", string.Empty);

                amountPages = GetAmountPages(lastPageRelativeUrl);
            }

            return new SearchResult(searchResultItems, amountPages, page);
        }

        private static int GetAmountPages(string relativeUrl)
        {
            var secondLastIndexOfSlash = relativeUrl.Substring(0, relativeUrl.Length - 1).LastIndexOf("/", StringComparison.Ordinal);

            var amountPages = relativeUrl.Substring(secondLastIndexOfSlash + 1, relativeUrl.Length - secondLastIndexOfSlash - 2);

            return int.Parse(amountPages);
        }
    }
}
