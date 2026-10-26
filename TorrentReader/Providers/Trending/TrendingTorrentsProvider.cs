using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Trending
{
    internal class TrendingTorrentsProvider : TorrentProvider, ITrendingTorrentsProvider
    {
        public TrendingTorrentsProvider(IHttpProvider httpProvider, IOverviewTransformer overviewTransformer) : base(httpProvider, overviewTransformer)
        {
        }

        public Task<IReadOnlyList<SearchResultItem>> GetAsync(TorrentPopularityRange popularityRange)
        {
            return Get(GetTrendingRelativeUrl(popularityRange));
        }

        private string GetTrendingRelativeUrl(TorrentPopularityRange movieRangeType)
        {
            if (movieRangeType == TorrentPopularityRange.Today)
            {
                return "trending";
            }

            return "trending-week";
        }
    }
}
