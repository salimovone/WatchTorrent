using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using TorrentReader.Config;
using TorrentReader.Http;
using TorrentReader.Search.Models;

namespace TorrentReader.Search
{
    internal class SearchProvider : ISearchProvider
    {
        private readonly IHttpProvider _httpProvider;

        private readonly ISearchTransformer _searchTransformer;

        public SearchProvider(IHttpProvider httpProvider, ISearchTransformer searchTransformer)
        {
            _httpProvider = httpProvider;
            _searchTransformer = searchTransformer;
        }

        public async Task<SearchResult> SearchAsync(string searchText, int page, SearchSortByType sort, SortOrderType order)
        {
            var searchUrl = GetSearchUrl(searchText, page, sort, order);
            var document = await _httpProvider.GetHtmlAsync(searchUrl).ConfigureAwait(false);

            return _searchTransformer.Transform(document, page);
        }

        private static string GetSortFilterUrlPart(SearchSortByType sortByType, SortOrderType orderByType)
        {
            var sorts = new Dictionary<SearchSortByType, string>
            {
                { SearchSortByType.Leechers, "leechers" },
                { SearchSortByType.Seeders,  "seeders" },
                { SearchSortByType.Time, "time" },
                { SearchSortByType.Size, "size" },
            };

            var orders = new Dictionary<SortOrderType, string>
            {
                { SortOrderType.Ascending, "asc" },
                { SortOrderType.Descending,  "desc" },
            };

            var sort = sorts[sortByType];
            var order = orders[orderByType];

            return $"{sort}/{order}";
        }

        private static string GetSearchUrl(string searchText, int page, SearchSortByType sortByType, SortOrderType orderByType)
        {
            var encodedSearchText = Uri.EscapeDataString(searchText);

            return $"{Configuration.BaseUrl}/sort-search/{encodedSearchText}/{GetSortFilterUrlPart(sortByType, orderByType)}/{page}/";
        }
    }
}
