using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Games.Popular
{
    internal class PopularGamesProvider : TorrentProvider, IPopularGamesProvider
    {
        public PopularGamesProvider(IHttpProvider httpProvider, IOverviewTransformer overviewTransformer) : base(httpProvider, overviewTransformer)
        {
        }

        public Task<IReadOnlyList<SearchResultItem>> GetAsync(TorrentPopularityRange movieRangeType)
        {
            return Get(GetPopularPath(movieRangeType));
        }

        private string GetPopularPath(TorrentPopularityRange popularRangeType)
        {
            if (popularRangeType == TorrentPopularityRange.Today)
            {
                return "popular-games";
            }

            return "popular-games-week";
        }
    }
}
