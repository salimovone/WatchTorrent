using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Config;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers
{
    internal abstract class TorrentProvider
    {
        private readonly IHttpProvider _httpProvider;

        private readonly IOverviewTransformer _overviewTransformer;

        public TorrentProvider(IHttpProvider httpProvider, IOverviewTransformer overviewTransformer)
        {
            _httpProvider = httpProvider;
            _overviewTransformer = overviewTransformer;
        }

        protected async Task<IReadOnlyList<SearchResultItem>> Get(string popularityRangePath)
        {
            var url = $"{Configuration.BaseUrl}/{popularityRangePath}";
            var document = await _httpProvider.GetHtmlAsync(url).ConfigureAwait(false);

            return _overviewTransformer.Transform(document);
        }
    }
}
