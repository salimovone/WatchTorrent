using MonoTorrent;
using MonoTorrent.PiecePicking;

namespace WatchTorrent.Services
{
    /// <summary>
    /// Yuklab olish va ko'rish uchun bitta umumiy bo'lak (piece) tanlash rejimi. MonoTorrent'ning StreamingPieceRequester'i
    /// har peer'dan bir vaqtda atigi 2–4 blok (16 KB) so'raydi — film ko'rilayotganda tezlik keskin tushadi, rejimni
    /// almashtirish esa torrentni to'xtatishni (barcha peerlar uziladi) talab qiladi. Bu yerda:
    ///  - har peer'ning so'rovlar navbati StandardPieceRequester'dagidek to'liq to'ldiriladi;
    ///  - bo'laklar boshidan ketma-ket yuklanadi — yuklab olishda tortilgan qism ko'rishda darhol ishlatiladi;
    ///  - film ko'rilayotganda o'qilayotgan joydan fayl oxirigacha birinchi navbatda yuklanadi, eng kerakli
    ///    bo'laklar esa bir vaqtda ikki peerdan so'raladi (bittasi sekin bo'lsa ham player kutib qolmasin).
    /// MonoTorrent metodlarni o'z oqimida chaqiradi; Seek/ReadToPosition esa player o'qiyotgan oqimdan keladi.
    /// </summary>
    public class PlaybackPieceRequester : IStreamingPieceRequester
    {
        // O'qilayotgan joydan keyingi shuncha bo'lak ikki peerdan parallel so'raladi.
        private const int UrgentPieces = 2;

        // Seek'dan keyin o'qish joyidan shu hajmdan uzoqdagi so'rovlar bekor qilinadi — yangi joy tezroq yuklansin.
        private const long KeepRequestsBytes = 64L * 1024 * 1024;

        // Eng kerakli bo'lakni olgan web-seed shuncha vaqt hech narsa yubormasa, bo'lak undan qaytarib olinadi.
        private const long StalledWebSeedMs = 5000;

        private readonly Dictionary<IRequester, long> _webSeedWaitingSince = new();
        private readonly HashSet<IRequester> _stalledWebSeeds = new();

        private IPieceRequesterData? _data;
        private IMessageEnqueuer? _enqueuer;
        private ReadOnlyBitField[] _ignorable = Array.Empty<ReadOnlyBitField>();
        private BitField? _filtered;
        private BitField? _interesting;
        private IPiecePicker? _sequential;
        private IPiecePicker? _rarest;
        private PieceSegment[] _requestBuffer = Array.Empty<PieceSegment>();
        private PieceSegment[] _cancellations = Array.Empty<PieceSegment>();
        private ReadOnlyBitField[] _otherAvailable = Array.Empty<ReadOnlyBitField>();

        // Player o'qiyotgan bo'lak va o'sha fayl oxiri; -1 — stream yo'q (oddiy yuklab olish).
        private volatile int _streamPiece = -1;
        private volatile int _streamEndPiece = -1;
        private volatile bool _seeked;

        public bool InEndgameMode { get; private set; }

        public void Initialise(IPieceRequesterData torrentData, IMessageEnqueuer enqueuer, ReadOnlySpan<ReadOnlyBitField> ignorableBitfields)
        {
            _data = torrentData;
            _enqueuer = enqueuer;
            _ignorable = ignorableBitfields.ToArray();
            _filtered = new BitField(torrentData.PieceCount);
            _interesting = new BitField(torrentData.PieceCount);
            InEndgameMode = false;

            // Ikkala tanlovchi bitta StandardPicker'ni bo'lishadi — kim nimani so'ragani bir joyda hisoblanadi.
            var standard = new StandardPicker();

            _sequential = new PriorityPicker(standard);
            _rarest = new PriorityPicker(new RarestFirstPicker(new RandomisedPicker(standard)));

            _sequential.Initialise(torrentData);
            _rarest.Initialise(torrentData);
        }

        /// <summary>Player yopildi — torrent yana boshidan ketma-ket yuklanadi.</summary>
        public void StopStreaming()
        {
            _streamPiece = -1;
            _streamEndPiece = -1;
            _seeked = false;
        }

        public void SeekToPosition(ITorrentManagerFile file, long position)
        {
            var previous = _streamPiece;

            ReadToPosition(file, position);

            if (previous != _streamPiece)
            {
                _seeked = true;
            }
        }

        public void ReadToPosition(ITorrentManagerFile file, long position)
        {
            if (_data == null)
            {
                return;
            }

            _streamEndPiece = file.EndPieceIndex;
            _streamPiece = position >= file.Length ? file.EndPieceIndex : _data.ByteOffsetToPieceIndex(file.OffsetInTorrent + position);
        }

        public void AddRequests(ReadOnlySpan<(IRequester Peer, ReadOnlyBitField Available)> peers)
        {
            if (_data == null)
            {
                return;
            }

            if (_otherAvailable.Length < peers.Length)
            {
                _otherAvailable = new ReadOnlyBitField[peers.Length];
            }

            var others = _otherAvailable.AsSpan(0, peers.Length);

            for (var i = 0; i < peers.Length; i++)
            {
                others[i] = peers[i].Available;
            }

            if (_seeked)
            {
                _seeked = false;
                CancelDistantRequests(peers);
            }

            ReclaimFromStalledWebSeeds(peers);

            foreach (var (peer, available) in peers)
            {
                AddRequests(peer, available, others);
            }
        }

        public void AddRequests(IRequester peer, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> allPeers)
        {
            if (!peer.CanRequestMorePieces || _data == null || _enqueuer == null || _sequential == null || _rarest == null)
            {
                return;
            }

            var last = _data.PieceCount - 1;
            var filtered = ApplyIgnorables(available, _filtered!);
            var streamPiece = _streamPiece;
            var streamEnd = Math.Min(_streamEndPiece, last);
            var isStreaming = streamPiece >= 0 && streamPiece <= streamEnd;
            var urgentEnd = isStreaming ? Math.Min(streamPiece + UrgentPieces - 1, streamEnd) : -1;

            // Web-seed (HTTP manba) har so'rovni alohida HTTP murojaatga aylantiradi — unga alohida yo'l.
            if (IsWebSeed(peer))
            {
                if (!isStreaming)
                {
                    RequestWebSeed(peer, filtered, allPeers, _sequential, 0, last);
                }
                else if (!_stalledWebSeeds.Contains(peer))
                {
                    // Eng kerakli bo'lak bittadan so'raladi: aks holda web-seed ketma-ket 4 ta bo'sh bo'lak qidiradi va
                    // atrofi yuklangan yakka bo'lakni (player aynan shuni kutayotgan bo'lsa ham) o'tkazib yuboradi.
                    _ = RequestWebSeed(peer, filtered, allPeers, _sequential, streamPiece, urgentEnd, _data.SegmentsPerPiece(streamPiece))
                        || RequestWebSeed(peer, filtered, allPeers, _sequential, streamPiece, streamEnd)
                        || RequestWebSeed(peer, filtered, allPeers, _rarest, 0, last);
                }

                return;
            }

            // Peer boshlagan bo'lakni oxiriga yetkazadi (bo'g'ayotgan peer faqat "fast" bo'laklarni davom ettira oladi).
            if (!peer.IsChoking || peer.SupportsFastPeer)
            {
                while (peer.CanRequestMorePieces && _sequential.ContinueExistingRequest(peer, 0, last, out var segment))
                {
                    _enqueuer.EnqueueRequest(peer, segment);
                }
            }

            if (isStreaming)
            {
                if (!peer.IsChoking)
                {
                    RequestDuplicates(peer, filtered, streamPiece, urgentEnd);
                }

                // O'qish joyidan fayl oxirigacha ketma-ket, keyin qolgan hamma narsa (avval kam tarqalganlari).
                Request(peer, filtered, allPeers, _sequential, streamPiece, streamEnd);
                Request(peer, filtered, allPeers, _rarest, 0, last);
            }
            else
            {
                Request(peer, filtered, allPeers, _sequential, 0, last);
            }

            // Hammasi so'ralgan, peer bo'sh qoldi: oxirgi bo'laklar boshqa peerlardan ham so'raladi (endgame).
            if (!peer.IsChoking && peer.AmRequestingPiecesCount == 0)
            {
                while (peer.CanRequestMorePieces)
                {
                    if (_sequential.ContinueAnyExistingRequest(peer, filtered, 0, last, 1, out var segment))
                    {
                        _enqueuer.EnqueueRequest(peer, segment);
                    }
                    else if ((InEndgameMode || available.AllTrue) && _sequential.ContinueAnyExistingRequest(peer, filtered, 0, last, 2, out segment))
                    {
                        InEndgameMode = true;
                        _enqueuer.EnqueueRequest(peer, segment);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        public bool ValidatePiece(IRequester peer, PieceSegment pieceSegment, out bool pieceComplete, HashSet<IRequester> peersInvolved)
        {
            pieceComplete = false;

            return _sequential != null && _sequential.ValidatePiece(peer, pieceSegment, out pieceComplete, peersInvolved);
        }

        public bool IsInteresting(IRequester peer, ReadOnlyBitField bitField)
        {
            return _sequential != null && _sequential.IsInteresting(peer, ApplyIgnorables(bitField, _interesting!));
        }

        public void CancelRequests(IRequester peer, int startIndex, int endIndex)
        {
            if (_sequential == null || _enqueuer == null)
            {
                return;
            }

            if (_cancellations.Length < peer.AmRequestingPiecesCount)
            {
                _cancellations = new PieceSegment[peer.AmRequestingPiecesCount];
            }

            var cancellations = _cancellations.AsSpan(0, peer.AmRequestingPiecesCount);
            var cancelled = _sequential.CancelRequests(peer, startIndex, endIndex, cancellations);

            _enqueuer.EnqueueCancellations(peer, cancellations.Slice(0, cancelled));
        }

        public void RequestRejected(IRequester peer, PieceSegment pieceRequest)
        {
            _sequential?.RequestRejected(peer, pieceRequest);
        }

        public int CurrentRequestCount()
        {
            return _sequential?.CurrentRequestCount() ?? 0;
        }

        /// <summary>Peer'ning navbati to'lguncha <paramref name="start"/>..<paramref name="end"/> oralig'idan bo'lak so'raydi.</summary>
        private void Request(IRequester peer, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> allPeers, IPiecePicker picker, int start, int end)
        {
            if (peer.IsChoking && !(peer.SupportsFastPeer && peer.IsAllowedFastPieces.Count > 0))
            {
                return;
            }

            // Oddiy peer uchun 1 blok — StandardPicker shunda oraliqdagi birinchi bo'sh bo'lakni oladi (qat'iy ketma-ket).
            var buffer = GetRequestBuffer(peer.PreferredRequestAmount(_data!.PieceLength));

            while (peer.CanRequestMorePieces)
            {
                var requested = picker.PickPiece(peer, available, allPeers, start, end, buffer);

                if (requested == 0)
                {
                    break;
                }

                _enqueuer!.EnqueueRequests(peer, buffer.Slice(0, requested));
            }
        }

        /// <summary>
        /// Web-seed'ga bitta so'rov (bitta HTTP murojaat) yuboradi; unda faqat uzluksiz bloklar bo'lishi shart. StandardPicker
        /// tashlab ketilgan bo'lakni (masalan javob bermayotgan boshqa web-seed'dan qolgan) bittadan blok qilib beradi —
        /// 16 KB'lik HTTP so'rovlar juda sekin, shuning uchun o'sha bo'lakning keyingi bloklari ham shu so'rovga qo'shiladi.
        /// </summary>
        /// <returns>So'rov yuborildimi.</returns>
        private bool RequestWebSeed(IRequester peer, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> allPeers, IPiecePicker picker, int start, int end, int maxBlocks = int.MaxValue)
        {
            if (peer.IsChoking || !peer.CanRequestMorePieces)
            {
                return false;
            }

            // ~2 MB (StandardPicker shuncha bo'lakni yonma-yon izlaydi); eng kerakli bo'lak uchun — bitta bo'lak.
            var buffer = GetRequestBuffer(Math.Min(peer.PreferredRequestAmount(_data!.PieceLength), maxBlocks));
            var requested = picker.PickPiece(peer, available, allPeers, start, end, buffer);

            if (requested == 0)
            {
                return false;
            }

            while (requested < buffer.Length)
            {
                var previous = buffer[requested - 1];

                if (!_sequential!.ContinueAnyExistingRequest(peer, available, previous.PieceIndex, previous.PieceIndex, 1, out var next))
                {
                    break;
                }

                if (next.PieceIndex != previous.PieceIndex || next.BlockIndex != previous.BlockIndex + 1)
                {
                    // Uzluksiz emas — bu blok keyingi so'rovga qoladi.
                    _sequential.RequestRejected(peer, next);
                    break;
                }

                buffer[requested++] = next;
            }

            _enqueuer!.EnqueueRequests(peer, buffer.Slice(0, requested));

            return true;
        }

        private Span<PieceSegment> GetRequestBuffer(int count)
        {
            if (_requestBuffer.Length < count)
            {
                _requestBuffer = new PieceSegment[count];
            }

            return _requestBuffer.AsSpan(0, count);
        }

        /// <summary>Boshqa peerga allaqachon so'ralgan, lekin hali kelmagan eng kerakli bloklarni shu peerdan ham so'raydi.</summary>
        private void RequestDuplicates(IRequester peer, ReadOnlyBitField available, int start, int end)
        {
            for (var i = 0; i < 4 && peer.CanRequestMorePieces; i++)
            {
                if (!_sequential!.ContinueAnyExistingRequest(peer, available, start, end, 2, out var segment))
                {
                    break;
                }

                _enqueuer!.EnqueueRequest(peer, segment);
            }
        }

        /// <summary>
        /// Seek'dan keyin peerlar navbatida eski joyning so'rovlari turibdi — ular yangi joy bloklaridan oldin keladi.
        /// Yangi joy hali yuklanmagan bo'lsa, undan uzoqdagi so'rovlar bekor qilinadi (keyin qayta so'raladi).
        /// </summary>
        private void CancelDistantRequests(ReadOnlySpan<(IRequester Peer, ReadOnlyBitField Available)> peers)
        {
            var start = _streamPiece;
            var fileEnd = _streamEndPiece;

            if (_data == null || start < 0 || !HasMissing(start, Math.Min(start + UrgentPieces - 1, fileEnd)))
            {
                return;
            }

            var last = _data.PieceCount - 1;
            var keepEnd = Math.Min(fileEnd, start + Math.Max(UrgentPieces, (int)(KeepRequestsBytes / _data.PieceLength)));

            foreach (var (peer, _) in peers)
            {
                if (!peer.CanCancelRequests)
                {
                    continue;
                }

                if (start > 0)
                {
                    CancelRequests(peer, 0, start - 1);
                }

                if (keepEnd < last)
                {
                    CancelRequests(peer, keepEnd + 1, last);
                }
            }
        }

        /// <summary>
        /// Javob bermayotgan web-seed (masalan o'chib ketgan server) eng kerakli bo'lakni o'z HTTP so'rovi tugaguncha
        /// (~30 s) band qilib turadi va player shuncha kutib qoladi. Bunday web-seed'dan bo'lak qaytarib olinadi va
        /// u ma'lumot yubora boshlaguncha stream paytida unga so'rov berilmaydi.
        /// </summary>
        private void ReclaimFromStalledWebSeeds(ReadOnlySpan<(IRequester Peer, ReadOnlyBitField Available)> peers)
        {
            var start = _streamPiece;
            var end = Math.Min(start + UrgentPieces - 1, _streamEndPiece);
            var hasWebSeed = false;

            foreach (var (peer, _) in peers)
            {
                hasWebSeed |= IsWebSeed(peer);
            }

            // Ma'lumot yubora boshlagan yoki uzilgan web-seed ro'yxatdan chiqariladi.
            if (_stalledWebSeeds.Count > 0)
            {
                var connected = new HashSet<IRequester>();

                foreach (var (peer, _) in peers)
                {
                    connected.Add(peer);
                }

                _stalledWebSeeds.RemoveWhere(peer => start < 0 || peer.DownloadSpeed > 0 || !connected.Contains(peer));
            }

            if (!hasWebSeed || start < 0 || _sequential == null || !HasMissing(start, end))
            {
                _webSeedWaitingSince.Clear();

                return;
            }

            var holders = new HashSet<IRequester>();

            foreach (var request in _sequential.ExportActiveRequests())
            {
                var piece = request.Request.PieceIndex;

                if (!request.Received && piece >= start && piece <= end && IsWebSeed(request.RequestedOff) && request.RequestedOff.DownloadSpeed == 0)
                {
                    holders.Add(request.RequestedOff);
                }
            }

            var now = Environment.TickCount64;

            foreach (var holder in holders)
            {
                if (!_webSeedWaitingSince.TryGetValue(holder, out var since))
                {
                    _webSeedWaitingSince[holder] = now;
                }
                else if (now - since > StalledWebSeedMs)
                {
                    CancelRequests(holder, start, end);
                    _stalledWebSeeds.Add(holder);
                    _webSeedWaitingSince.Remove(holder);
                }
            }

            foreach (var peer in _webSeedWaitingSince.Keys.Where(peer => !holders.Contains(peer)).ToList())
            {
                _webSeedWaitingSince.Remove(peer);
            }
        }

        private bool IsWebSeed(IRequester peer)
        {
            return peer.PreferredRequestAmount(_data!.PieceLength) > 1;
        }

        /// <summary>Oraliqda hali yuklanmagan (va tekshiruvni kutmayotgan) bo'lak bormi.</summary>
        private bool HasMissing(int start, int end)
        {
            for (var piece = start; piece <= end; piece++)
            {
                if (!_ignorable.Any(bitfield => bitfield[piece]))
                {
                    return true;
                }
            }

            return false;
        }

        private ReadOnlyBitField ApplyIgnorables(ReadOnlyBitField available, BitField target)
        {
            target.From(available);

            foreach (var ignorable in _ignorable)
            {
                target.NAnd(ignorable);
            }

            return target;
        }
    }
}
