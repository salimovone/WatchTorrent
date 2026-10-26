using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Movies.Top
{
    internal class TopHundredMovieProvider : TorrentProvider, ITopHundredMovieProvider
    {
        public TopHundredMovieProvider(IHttpProvider httpProvider, IOverviewTransformer overviewTransformer) : base(httpProvider, overviewTransformer)
        {
        }

        public Task<IReadOnlyList<SearchResultItem>> GetAsync()
        {
            return Get("top-100-movies");
        }
    }
}
