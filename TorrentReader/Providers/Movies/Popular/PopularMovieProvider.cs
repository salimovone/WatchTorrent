using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Movies.Popular
{
    internal class PopularMovieProvider : TorrentProvider, IPopularMovieProvider
    {
        public PopularMovieProvider(IHttpProvider httpProvider, IOverviewTransformer overviewTransformer) : base(httpProvider, overviewTransformer)
        {
        }

        public Task<IReadOnlyList<SearchResultItem>> GetMoviesAsync(TorrentPopularityRange movieRangeType)
        {
            return Get(GetPopularPath(movieRangeType));
        }

        public Task<IReadOnlyList<SearchResultItem>> GetForeignMoviesAsync(TorrentPopularityRange movieRangeType)
        {
            return Get(GetPopularForeignPath(movieRangeType));
        }

        private string GetPopularPath(TorrentPopularityRange popularRangeType)
        {
            if (popularRangeType == TorrentPopularityRange.Today)
            {
                return "popular-movies";
            }

            return "popular-movies-week";
        }

        private string GetPopularForeignPath(TorrentPopularityRange movieRangeType)
        {
            if (movieRangeType == TorrentPopularityRange.Today)
            {
                return "popular-foreign-movies";
            }

            return "popular-foreign-movies-week";
        }
    }
}
