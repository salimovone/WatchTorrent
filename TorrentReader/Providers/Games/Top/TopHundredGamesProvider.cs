using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Games.Top
{
    internal class TopHundredGamesProvider : TorrentProvider, ITopHundredGamesProvider
    {
        public TopHundredGamesProvider(IHttpProvider httpProvider, IOverviewTransformer overviewTransformer) : base(httpProvider, overviewTransformer)
        {
        }

        public Task<IReadOnlyList<SearchResultItem>> GetAsync()
        {
            return Get("top-100-games");
        }
    }
}
