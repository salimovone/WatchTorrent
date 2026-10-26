using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Games.Trending
{
    internal interface ITrendingGamesProvider
    {
        Task<IReadOnlyList<SearchResultItem>> GetAsync(TorrentPopularityRange movieRangeType);
    }
}