using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Movies.Trending
{
    internal interface ITrendingMovieProvider
    {
        Task<IReadOnlyList<SearchResultItem>> GetAsync(TorrentPopularityRange movieRangeType);
    }
}