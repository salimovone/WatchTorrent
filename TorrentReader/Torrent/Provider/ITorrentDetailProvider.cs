using System.Threading.Tasks;

namespace TorrentReader.Torrent.Provider
{
    internal interface ITorrentDetailProvider
    {
        Task<Models.Torrent> GetAsync(int torrentId, string slug);
    }
}