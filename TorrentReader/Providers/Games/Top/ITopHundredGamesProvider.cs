using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Search.Models;

namespace TorrentReader.Providers.Games.Top
{
    internal interface ITopHundredGamesProvider
    {
        Task<IReadOnlyList<SearchResultItem>> GetAsync();
    }
}