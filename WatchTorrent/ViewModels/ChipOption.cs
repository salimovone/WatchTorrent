using TorrentReader;
using TorrentReader.Providers;
using TorrentReader.Search;
using TorrentReader.Search.Models;

namespace WatchTorrent.ViewModels
{
    /// <summary>Gorizontal "chip" ro'yxatidagi bitta tanlov (ChipTemplate shu turga bog'langan).</summary>
    public class ChipOption
    {
        public ChipOption(string title)
        {
            Title = title;
        }

        public string Title { get; }
    }

    public class DiscoverCategory : ChipOption
    {
        public DiscoverCategory(
            string title,
            bool hasRange,
            Func<TorrentClient, TorrentPopularityRange, Task<IReadOnlyList<SearchResultItem>>> load)
            : base(title)
        {
            HasRange = hasRange;
            Load = load;
        }

        /// <summary>Bugun/Hafta tanlovi bormi (Top 100 ro'yxatlarida yo'q).</summary>
        public bool HasRange { get; }

        public Func<TorrentClient, TorrentPopularityRange, Task<IReadOnlyList<SearchResultItem>>> Load { get; }
    }

    public class RangeOption : ChipOption
    {
        public RangeOption(string title, TorrentPopularityRange range)
            : base(title)
        {
            Range = range;
        }

        public TorrentPopularityRange Range { get; }
    }

    public class SortOption : ChipOption
    {
        public SortOption(string title, SearchSortByType sortBy, SortOrderType order)
            : base(title)
        {
            SortBy = sortBy;
            Order = order;
        }

        public SearchSortByType SortBy { get; }

        public SortOrderType Order { get; }
    }
}
