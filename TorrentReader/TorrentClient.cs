using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Providers;
using TorrentReader.Providers.Games.Popular;
using TorrentReader.Providers.Games.Top;
using TorrentReader.Providers.Games.Trending;
using TorrentReader.Providers.Movies.Popular;
using TorrentReader.Providers.Movies.Top;
using TorrentReader.Providers.Movies.Trending;
using TorrentReader.Providers.Trending;
using TorrentReader.Search;
using TorrentReader.Search.Models;
using TorrentReader.Torrent.Models;
using TorrentReader.Torrent.Provider;
using TorrentReader.Torrent.Transformer;
using TorrentModel = TorrentReader.Torrent.Models.Torrent;

namespace TorrentReader
{
    /// <summary>
    /// 1337x.to dan ma'lumot olish uchun yagona kirish nuqtasi.
    /// <code>
    /// var reader = new TorrentReader(CloudflareWebViewSolver.SolveAsync);
    /// var results = await reader.SearchAsync("ubuntu");
    /// var torrent = await reader.GetTorrentAsync(results.Items[0]);
    /// </code>
    /// </summary>
    public class TorrentClient
    {
        private readonly IHttpProvider _http;
        private readonly ISearchProvider _search;
        private readonly ITorrentDetailProvider _detail;
        private readonly IPopularMovieProvider _popularMovies;
        private readonly ITrendingMovieProvider _trendingMovies;
        private readonly ITopHundredMovieProvider _topMovies;
        private readonly IPopularGamesProvider _popularGames;
        private readonly ITrendingGamesProvider _trendingGames;
        private readonly ITopHundredGamesProvider _topGames;
        private readonly ITrendingTorrentsProvider _trending;

        /// <param name="cloudflareSolver">
        /// Cloudflare tekshiruvi chiqqanda chaqiriladi (MAUI'da WebView ochib cf_clearance oladi).
        /// Faqat cookie yo'q yoki eskirgan paytda ishlaydi, qolgan so'rovlar oddiy HttpClient orqali.
        /// null bo'lsa tekshiruvda <see cref="CloudflareChallengeException"/> tashlanadi.
        /// </param>
        public TorrentClient(Func<Uri, Task<CloudflareClearance>> cloudflareSolver = null)
        {
            var overview = new OverviewTransformer();

            _http = new HttpProvider(cloudflareSolver);
            _search = new SearchProvider(_http, new SearchTransformer(overview));
            _detail = new TorrentDetailProvider(_http, new TorrentDetailTransformer());
            _popularMovies = new PopularMovieProvider(_http, overview);
            _trendingMovies = new TrendingMovieProvider(_http, overview);
            _topMovies = new TopHundredMovieProvider(_http, overview);
            _popularGames = new PopularGamesProvider(_http, overview);
            _trendingGames = new TrendingGamesProvider(_http, overview);
            _topGames = new TopHundredGamesProvider(_http, overview);
            _trending = new TrendingTorrentsProvider(_http, overview);
        }

        /// <summary>
        /// Joriy Cloudflare cookie + User-Agent. Ilova yopilganda saqlab, keyingi ishga tushishda
        /// qayta berilsa WebView har safar ochilmaydi.
        /// </summary>
        public CloudflareClearance Clearance
        {
            get => _http.Clearance;
            set => _http.Clearance = value;
        }

        // ---- Qidiruv ----

        /// <summary>Torrentlarni nom bo'yicha qidiradi (page 1 dan boshlanadi).</summary>
        public Task<SearchResult> SearchAsync(
            string query,
            int page = 1,
            SearchSortByType sortBy = SearchSortByType.Time,
            SortOrderType order = SortOrderType.Descending)
        {
            return _search.SearchAsync(query, page, sortBy, order);
        }

        // ---- Torrent ma'lumoti ----

        /// <summary>Torrent haqida to'liq ma'lumot (tavsif, magnet, fayllar, izohlar).</summary>
        public Task<TorrentModel> GetTorrentAsync(int torrentId, string slug)
        {
            return _detail.GetAsync(torrentId, slug);
        }

        /// <summary>Qidiruv/ro'yxat natijasidagi elementdan to'g'ridan-to'g'ri torrent ma'lumotini oladi.</summary>
        public Task<TorrentModel> GetTorrentAsync(SearchResultItem item)
        {
            return _detail.GetAsync(item.TorrentId, item.Slug);
        }

        // ---- Umumiy trendlar ----

        public Task<IReadOnlyList<SearchResultItem>> GetTrendingAsync(TorrentPopularityRange range = TorrentPopularityRange.Today)
        {
            return _trending.GetAsync(range);
        }

        // ---- Kinolar ----

        public Task<IReadOnlyList<SearchResultItem>> GetPopularMoviesAsync(TorrentPopularityRange range = TorrentPopularityRange.Today)
        {
            return _popularMovies.GetMoviesAsync(range);
        }

        public Task<IReadOnlyList<SearchResultItem>> GetPopularForeignMoviesAsync(TorrentPopularityRange range = TorrentPopularityRange.Today)
        {
            return _popularMovies.GetForeignMoviesAsync(range);
        }

        public Task<IReadOnlyList<SearchResultItem>> GetTrendingMoviesAsync(TorrentPopularityRange range = TorrentPopularityRange.Today)
        {
            return _trendingMovies.GetAsync(range);
        }

        public Task<IReadOnlyList<SearchResultItem>> GetTop100MoviesAsync()
        {
            return _topMovies.GetAsync();
        }

        // ---- O'yinlar ----

        public Task<IReadOnlyList<SearchResultItem>> GetPopularGamesAsync(TorrentPopularityRange range = TorrentPopularityRange.Today)
        {
            return _popularGames.GetAsync(range);
        }

        public Task<IReadOnlyList<SearchResultItem>> GetTrendingGamesAsync(TorrentPopularityRange range = TorrentPopularityRange.Today)
        {
            return _trendingGames.GetAsync(range);
        }

        public Task<IReadOnlyList<SearchResultItem>> GetTop100GamesAsync()
        {
            return _topGames.GetAsync();
        }
    }
}
