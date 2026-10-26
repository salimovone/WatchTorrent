using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Games.Trending
{
    internal class TrendingGamesProvider : TorrentProvider, ITrendingGamesProvider
    {
        public TrendingGamesProvider(IHttpProvider httpProvider, IOverviewTransformer overviewTransformer) : base(httpProvider, overviewTransformer)
        {
        }

        public Task<IReadOnlyList<SearchResultItem>> GetAsync(TorrentPopularityRange torrentPopularityRange)
        {
            return Get(GetMoviesRelativeUrl(torrentPopularityRange));
        }

        private string GetMoviesRelativeUrl(TorrentPopularityRange movieRangeType)
        {
            if (movieRangeType == TorrentPopularityRange.Today)
            {
                return "trending/d/games/";
            }

            return "trending/w/games/";
        }
    }
}
