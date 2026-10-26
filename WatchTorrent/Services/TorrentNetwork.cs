using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;
using MonoTorrent.Dht;

namespace WatchTorrent.Services
{
    /// <summary>
    /// Peerlarni tezroq topish uchun tarmoq sozlamalari.
    /// MonoTorrent 3.0.2 da ikki muammo bor:
    ///  - DHT faqat router.bittorrent.com orqali ishga tushadi (u javob bermasa DHT umuman ishlamaydi);
    ///  - DHT keshini bencoded ro'yxat qilib yozadi, lekin o'qishda xom bayt deb o'qiydi — kesh hech qachon ishlamaydi.
    /// Shuning uchun bootstrap tugunlari va kesh shu yerda boshqariladi.
    /// </summary>
    public static class TorrentNetwork
    {
        /// <summary>Ishonchli ommaviy trackerlar — magnet'dagi trackerlar o'lik bo'lsa ham peer topiladi.</summary>
        public static readonly IReadOnlyList<string> PublicTrackers = new[]
        {
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://open.stealth.si:80/announce",
            "udp://tracker.torrent.eu.org:451/announce",
            "udp://exodus.desync.com:6969/announce",
            "udp://open.demonii.com:1337/announce",
            "udp://explodie.org:6969/announce",
            "udp://tracker.dler.org:6969/announce",
            "udp://tracker.tiny-vps.com:6969/announce",
            "http://tracker.opentrackr.org:1337/announce",
            "http://tracker.bt4g.com:2095/announce"
        };

        private static readonly (string Host, int Port)[] BootstrapRouters =
        {
            ("router.bittorrent.com", 6881),
            ("router.utorrent.com", 6881),
            ("dht.transmissionbt.com", 6881),
            ("dht.libtorrent.org", 25401),
            ("dht.aelitis.com", 6881)
        };

        /// <summary>Saqlangan DHT tugunlari + bootstrap routerlari (compact node formatida).</summary>
        public static async Task<IReadOnlyList<ReadOnlyMemory<byte>>> GetBootstrapNodesAsync(string cachePath)
        {
            var nodes = LoadCachedNodes(cachePath);

            var lookups = BootstrapRouters.Select(async router =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    var addresses = await Dns.GetHostAddressesAsync(router.Host, timeout.Token);

                    return addresses
                        .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(address => CreateCompactNode(address, router.Port))
                        .ToList();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DHT] {router.Host} topilmadi: {ex.Message}");

                    return new List<ReadOnlyMemory<byte>>();
                }
            });

            foreach (var resolved in await Task.WhenAll(lookups))
            {
                nodes.AddRange(resolved);
            }

            return nodes;
        }

        public static async Task SaveNodesAsync(DhtEngine dht, string cachePath)
        {
            var data = await dht.SaveNodesAsync();

            if (data.Length > 0)
            {
                await File.WriteAllBytesAsync(cachePath, data.ToArray());
            }
        }

        /// <summary>
        /// Magnet'dagi barcha trackerlar bitta tier'ga tushadi va MonoTorrent ularni navbat bilan (o'lik tracker
        /// timeout bo'lguncha kutib) sinaydi. Har bir tracker alohida tier qilinadi — hammasi parallel so'raladi.
        /// </summary>
        public static async Task SpreadTrackersAsync(TorrentManager manager)
        {
            var trackerManager = manager.TrackerManager;

            if (trackerManager.Private)
            {
                return;
            }

            var current = trackerManager.Tiers.SelectMany(tier => tier.Trackers).ToList();
            var uris = current
                .Select(tracker => tracker.Uri)
                .Concat(PublicTrackers.Select(url => new Uri(url)))
                .Distinct()
                .ToList();

            foreach (var tracker in current)
            {
                await trackerManager.RemoveTrackerAsync(tracker);
            }

            foreach (var uri in uris)
            {
                try
                {
                    await trackerManager.AddTrackerAsync(uri);
                }
                catch (Exception ex)
                {
                    // Masalan wss:// — MonoTorrent qo'llamaydi.
                    Debug.WriteLine($"[Trackers] {uri} qo'shilmadi: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Magnet'dagi web-seed (HTTP manba) manzillari: "ws" (BEP 19) va "as". MonoTorrent 3.0.2 faqat "as"ni o'qiydi,
        /// "ws" esa (masalan archive.org magnetlarida) jimgina tashlab yuboriladi.
        /// </summary>
        public static IReadOnlyList<string> GetWebSeeds(string magnetUri)
        {
            var queryStart = magnetUri.IndexOf('?');

            if (queryStart < 0)
            {
                return Array.Empty<string>();
            }

            return magnetUri[(queryStart + 1)..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(pair => pair.Length == 2 && pair[0] is "ws" or "as")
                .Select(pair => Uri.UnescapeDataString(pair[1]))
                .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                .Distinct()
                .ToList();
        }

        private static List<ReadOnlyMemory<byte>> LoadCachedNodes(string cachePath)
        {
            try
            {
                if (!File.Exists(cachePath))
                {
                    return new List<ReadOnlyMemory<byte>>();
                }

                return BEncodedValue.Decode<BEncodedList>(File.ReadAllBytes(cachePath))
                    .OfType<BEncodedString>()
                    .Select(node => node.AsMemory())
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DHT] Kesh o'qilmadi: {ex.Message}");

                return new List<ReadOnlyMemory<byte>>();
            }
        }

        /// <summary>20 bayt tasodifiy node id + 4 bayt IPv4 + 2 bayt port (BEP 5).</summary>
        private static ReadOnlyMemory<byte> CreateCompactNode(IPAddress address, int port)
        {
            var node = new byte[26];

            Random.Shared.NextBytes(node.AsSpan(0, 20));
            address.GetAddressBytes().CopyTo(node, 20);
            node[24] = (byte)(port >> 8);
            node[25] = (byte)port;

            return node;
        }
    }
}
