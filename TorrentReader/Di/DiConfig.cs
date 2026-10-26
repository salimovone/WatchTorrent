using Microsoft.Extensions.DependencyInjection;
using TorrentReader.Http;
using TorrentReader.Overview;
using TorrentReader.Providers.Games.Popular;
using TorrentReader.Providers.Games.Top;
using TorrentReader.Providers.Games.Trending;
using TorrentReader.Providers.Movies.Popular;
using TorrentReader.Providers.Movies.Top;
using TorrentReader.Providers.Movies.Trending;
using TorrentReader.Providers.Trending;
using TorrentReader.Search;
using TorrentReader.Torrent.Provider;
using TorrentReader.Torrent.Transformer;

namespace TorrentReader.Di
{
    internal static class DiConfig
    {
        public static void ConfigureTorrentReaderServices(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<IHttpProvider, HttpProvider>();

            serviceCollection.AddTransient<IPopularMovieProvider, PopularMovieProvider>();
            serviceCollection.AddTransient<ITrendingMovieProvider, TrendingMovieProvider>();
            serviceCollection.AddTransient<ITopHundredMovieProvider, TopHundredMovieProvider>();

            serviceCollection.AddTransient<IPopularGamesProvider, PopularGamesProvider>();
            serviceCollection.AddTransient<ITrendingGamesProvider, TrendingGamesProvider>();
            serviceCollection.AddTransient<ITopHundredGamesProvider, TopHundredGamesProvider>();

            serviceCollection.AddTransient<ITrendingTorrentsProvider, TrendingTorrentsProvider>();

            serviceCollection.AddTransient<IOverviewTransformer, OverviewTransformer>();

            serviceCollection.AddTransient<ISearchProvider, SearchProvider>();
            serviceCollection.AddTransient<ISearchTransformer, SearchTransformer>();

            serviceCollection.AddTransient<ITorrentDetailProvider, TorrentDetailProvider>();
            serviceCollection.AddTransient<ITorrentDetailTransformer, TorrentDetailTransformer>();
        }
    }
}
