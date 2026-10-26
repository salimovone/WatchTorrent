using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Movies.Popular
{
    internal interface IPopularMovieProvider
    {
        Task<IReadOnlyList<SearchResultItem>> GetMoviesAsync(TorrentPopularityRange movieRangeType);

        Task<IReadOnlyList<SearchResultItem>> GetForeignMoviesAsync(TorrentPopularityRange movieRangeType);
    }
}