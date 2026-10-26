using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Movies.Trending
{
    internal class TrendingMovieProvider : TorrentProvider, ITrendingMovieProvider
    {
        public TrendingMovieProvider(IHttpProvider httpProvider, IOverviewTransformer overviewTransformer) : base(httpProvider, overviewTransformer)
        {
        }

        public Task<IReadOnlyList<SearchResultItem>> GetAsync(TorrentPopularityRange movieRangeType)
        {
            return Get(GetMoviesRelativeUrl(movieRangeType));
        }

        private string GetMoviesRelativeUrl(TorrentPopularityRange movieRangeType)
        {
            if (movieRangeType == TorrentPopularityRange.Today)
            {
                return "trending/d/movies/";
            }

            return "trending/w/movies/";
        }
    }
}
