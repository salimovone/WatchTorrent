using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Games.Popular
{
    internal interface IPopularGamesProvider
    {
        Task<IReadOnlyList<SearchResultItem>> GetAsync(TorrentPopularityRange movieRangeType);
    }
}