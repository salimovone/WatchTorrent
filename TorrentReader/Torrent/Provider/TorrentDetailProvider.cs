using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TorrentReader.Config;
using TorrentReader.Http;
using TorrentReader.Torrent.Models;
using TorrentReader.Torrent.Transformer;

namespace TorrentReader.Torrent.Provider
{
    internal class TorrentDetailProvider : ITorrentDetailProvider
    {
        private readonly IHttpProvider _httpProvider;

        private readonly ITorrentDetailTransformer _torrentDetailTransformer;

        public TorrentDetailProvider(IHttpProvider httpProvider, ITorrentDetailTransformer torrentDetailTransformer)
        {
            _httpProvider = httpProvider;
            _torrentDetailTransformer = torrentDetailTransformer;
        }

        public async Task<Models.Torrent> GetAsync(int torrentId, string slug)
        {
            var torrentUrl = $"{Configuration.BaseUrl}/torrent/{torrentId}/{slug}/";

            var documentTask = _httpProvider.GetHtmlAsync(torrentUrl).ConfigureAwait(false);
            var commentsTask = GetCommentsAsync(torrentId).ConfigureAwait(false);

            var document = await documentTask;
            var comments = await commentsTask;

            var detail = _torrentDetailTransformer.Transform(document);

            return new Models.Torrent(detail, comments);
        }

        private async Task<IReadOnlyList<TorrentComment>> GetCommentsAsync(int torrentId)
        {
            var commentsUrl = $"/comments.php?torrentid={torrentId}";

            var comments = await _httpProvider.GetAsync<TorrentComment>(commentsUrl);

            if(comments == null)
            {
                return new List<TorrentComment>();
            }

            return comments
                .Select(comment => new TorrentComment(
                    comment.CommentId,
                    comment.Username,
                    comment.Comment,
                    comment.Posted,
                    comment.Avatar.StartsWith("/") ? $"{Configuration.BaseUrl}{comment.Avatar}" : comment.Avatar)
            ).ToList();
        }
    }
}
