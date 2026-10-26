using System.Collections.Generic;

namespace TorrentReader.Search.Models
{
    public class SearchResult
    {
        public IReadOnlyList<SearchResultItem> Items { get; }

        public int AmountPages { get; set; }

        public int CurrentPage { get; set; }

        public SearchResult(IReadOnlyList<SearchResultItem> items, int amountPages, int currentPage)
        {
            Items = items;
            AmountPages = amountPages;
            CurrentPage = currentPage;
        }
    }
}
