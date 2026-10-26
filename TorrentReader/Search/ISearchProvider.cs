using System.Threading.Tasks;
using TorrentReader.Search.Models;

namespace TorrentReader.Search
{
    internal interface ISearchProvider
    {
        Task<SearchResult> SearchAsync(string searchText, int page, SearchSortByType sort, SortOrderType order);
    }
}