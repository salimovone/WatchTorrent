using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Trending
{
    internal interface ITrendingTorrentsProvider
    {
        Task<IReadOnlyList<SearchResultItem>> GetAsync(TorrentPopularityRange popularityRange);
    }
}