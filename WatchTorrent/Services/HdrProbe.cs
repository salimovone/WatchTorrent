using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace WatchTorrent.Services
{
    /// <summary>Videoning dinamik diapazoni.</summary>
    public enum DynamicRange
    {
        Sdr,
        Hdr10,
        Hdr10Plus,
        Hlg,
        DolbyVision
    }

    /// <summary>
    /// Video HDR ekanini fayl sarlavhasidan aniqlaydi (LibVLC 3 API videoning rang ma'lumotini bermaydi):
    /// MKV — Colour elementi va Dolby Vision konfiguratsiyasi, MP4 — colr (nclx) va dvcC/dvvC qutilari.
    /// Konteynerda rang ma'lumoti bo'lmasa HEVC SPS'dagi VUI o'qiladi, u ham bo'lmasa fayl nomiga qaraladi
    /// ("HDR10", "DV", "HLG"...).
    /// </summary>
    public static class HdrProbe
    {
        // MKV sarlavhasi (Tracks) odatda faylning birinchi bir necha KB'ida bo'ladi.
        private const int MatroskaHeadSize = 4 * 1024 * 1024;

        // MP4 moov qutisidan faqat trak/stsd kerak — undan kattasi o'qilmaydi.
        private const int MaxBoxSize = 64 * 1024 * 1024;

        // ITU-T H.273: transfer_characteristics
        private const int TransferPq = 16;
        private const int TransferHlg = 18;

        private static readonly Regex NameHdr10Plus = new(@"(?<![a-z0-9])hdr10(\+|plus)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NameDolbyVision = new(@"(?<![a-z0-9])(dv|dovi|dolby[ ._-]?vision)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NameHlg = new(@"(?<![a-z0-9])hlg(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NameHdr = new(@"(?<![a-z0-9])hdr(10)?(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Faylni tekshiradi. Kerakli qism hali yuklanmagan bo'lsa (<paramref name="isAvailable"/> false) — null:
        /// keyinroq qayta urinib ko'rish kerak.
        /// </summary>
        /// <param name="isAvailable">Fayldagi (offset, uzunlik) oralig'i diskda bormi; null — fayl to'liq.</param>
        public static DynamicRange? Detect(string path, Func<long, long, bool>? isAvailable = null)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var reader = new RangeReader(stream, isAvailable ?? ((_, _) => true));
                var info = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".mkv" or ".webm" or ".mk3d" => ReadMatroska(reader),
                    ".mp4" or ".m4v" or ".mov" => ReadMp4(reader),
                    _ => new ColourInfo()
                };

                return info == null ? null : Classify(info, Path.GetFileName(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static string GetLabel(DynamicRange range)
        {
            return range switch
            {
                DynamicRange.Hdr10 => "HDR10",
                DynamicRange.Hdr10Plus => "HDR10+",
                DynamicRange.Hlg => "HLG",
                DynamicRange.DolbyVision => "Dolby Vision",
                _ => "SDR"
            };
        }

        private static DynamicRange Classify(ColourInfo info, string name)
        {
            if (info.HasDolbyVision)
            {
                return DynamicRange.DolbyVision;
            }

            switch (info.Transfer)
            {
                case TransferPq:
                    // HDR10+ dinamik metama'lumoti har kadrda (SEI) — sarlavhadan ko'rinmaydi, nomdan olinadi.
                    return NameHdr10Plus.IsMatch(name) ? DynamicRange.Hdr10Plus : DynamicRange.Hdr10;
                case TransferHlg:
                    return DynamicRange.Hlg;
                case not null and not 2:
                    // Rang ma'lumoti aniq yozilgan va u HDR emas (2 — "ko'rsatilmagan").
                    return DynamicRange.Sdr;
            }

            // Sarlavhada ma'lumot yo'q — release nomidagi belgilar.
            return NameHdr10Plus.IsMatch(name) ? DynamicRange.Hdr10Plus
                : NameDolbyVision.IsMatch(name) ? DynamicRange.DolbyVision
                : NameHlg.IsMatch(name) ? DynamicRange.Hlg
                : NameHdr.IsMatch(name) ? DynamicRange.Hdr10
                : DynamicRange.Sdr;
        }

        #region Matroska

        private const uint EbmlHeaderId = 0x1A45DFA3;
        private const uint SegmentId = 0x18538067;
        private const uint ClusterId = 0x1F43B675;
        private const uint TracksId = 0x1654AE6B;
        private const uint TrackEntryId = 0xAE;
        private const uint TrackTypeId = 0x83;
        private const uint CodecIdId = 0x86;
        private const uint CodecPrivateId = 0x63A2;
        private const uint VideoId = 0xE0;
        private const uint ColourId = 0x55B0;
        private const uint TransferCharacteristicsId = 0x55BA;
        private const uint BlockAdditionMappingId = 0x41E4;
        private const uint BlockAddIdTypeId = 0x41E7;

        private static ColourInfo? ReadMatroska(RangeReader reader)
        {
            var head = reader.Read(0, (int)Math.Min(MatroskaHeadSize, reader.Length));

            if (head == null)
            {
                return null;
            }

            var position = 0;

            if (!TryReadElement(head, ref position, out var id, out var size) || id != EbmlHeaderId)
            {
                return new ColourInfo();
            }

            position += (int)size;

            if (!TryReadElement(head, ref position, out id, out _) || id != SegmentId)
            {
                return new ColourInfo();
            }

            // Segment ichidagi elementlar: Tracks topilguncha (klasterlardan oldin keladi).
            while (TryReadElement(head, ref position, out id, out size))
            {
                if (id == ClusterId)
                {
                    break;
                }

                if (size < 0 || position + size > head.Length)
                {
                    // Element o'qilgan qismdan tashqariga chiqdi — Tracks'gacha yetib bo'lmadi.
                    return id == TracksId ? null : new ColourInfo();
                }

                if (id == TracksId)
                {
                    return ReadTracks(head.AsSpan(position, (int)size));
                }

                position += (int)size;
            }

            return new ColourInfo();
        }

        private static ColourInfo ReadTracks(ReadOnlySpan<byte> tracks)
        {
            var position = 0;

            while (TryReadElement(tracks, ref position, out var id, out var size) && size >= 0 && position + size <= tracks.Length)
            {
                if (id == TrackEntryId && ReadTrackEntry(tracks.Slice(position, (int)size)) is { } info)
                {
                    // Birinchi video yo'lagi.
                    return info;
                }

                position += (int)size;
            }

            return new ColourInfo();
        }

        private static ColourInfo? ReadTrackEntry(ReadOnlySpan<byte> entry)
        {
            var info = new ColourInfo();
            var isVideo = false;
            var codec = string.Empty;
            byte[]? codecPrivate = null;
            var position = 0;

            while (TryReadElement(entry, ref position, out var id, out var size) && size >= 0 && position + size <= entry.Length)
            {
                var data = entry.Slice(position, (int)size);

                switch (id)
                {
                    case TrackTypeId:
                        isVideo = ReadUInt(data) == 1;
                        break;
                    case CodecIdId:
                        codec = Encoding.ASCII.GetString(data).TrimEnd('\0');
                        break;
                    case CodecPrivateId:
                        codecPrivate = data.ToArray();
                        break;
                    case VideoId:
                        ReadVideo(data, info);
                        break;
                    case BlockAdditionMappingId:
                        ReadBlockAdditionMapping(data, info);
                        break;
                }

                position += (int)size;
            }

            if (!isVideo)
            {
                return null;
            }

            if (info.Transfer == null && codecPrivate != null && codec == "V_MPEGH/ISO/HEVC")
            {
                info.Transfer = Hevc.ReadTransferFromConfig(codecPrivate);
            }

            return info;
        }

        private static void ReadVideo(ReadOnlySpan<byte> video, ColourInfo info)
        {
            var position = 0;

            while (TryReadElement(video, ref position, out var id, out var size) && size >= 0 && position + size <= video.Length)
            {
                if (id == ColourId)
                {
                    var colour = video.Slice(position, (int)size);
                    var inner = 0;

                    while (TryReadElement(colour, ref inner, out var colourId, out var colourSize) && colourSize >= 0 && inner + colourSize <= colour.Length)
                    {
                        if (colourId == TransferCharacteristicsId)
                        {
                            info.Transfer = (int)ReadUInt(colour.Slice(inner, (int)colourSize));
                        }

                        inner += (int)colourSize;
                    }
                }

                position += (int)size;
            }
        }

        private static void ReadBlockAdditionMapping(ReadOnlySpan<byte> mapping, ColourInfo info)
        {
            var position = 0;

            while (TryReadElement(mapping, ref position, out var id, out var size) && size >= 0 && position + size <= mapping.Length)
            {
                // 'dvcC' / 'dvvC' — Dolby Vision konfiguratsiyasi.
                if (id == BlockAddIdTypeId && ReadUInt(mapping.Slice(position, (int)size)) is 0x64766343 or 0x64767643)
                {
                    info.HasDolbyVision = true;
                }

                position += (int)size;
            }
        }

        /// <summary>EBML element sarlavhasi: ID (belgi bitlari bilan) va hajm (noma'lum hajm — -1).</summary>
        private static bool TryReadElement(ReadOnlySpan<byte> data, ref int position, out uint id, out long size)
        {
            id = 0;
            size = -1;

            if (position >= data.Length || data[position] == 0)
            {
                return false;
            }

            var idLength = System.Numerics.BitOperations.LeadingZeroCount((uint)data[position]) - 24 + 1;

            if (idLength > 4 || position + idLength >= data.Length)
            {
                return false;
            }

            for (var i = 0; i < idLength; i++)
            {
                id = (id << 8) | data[position + i];
            }

            position += idLength;

            var first = data[position];

            if (first == 0)
            {
                return false;
            }

            var sizeLength = System.Numerics.BitOperations.LeadingZeroCount((uint)first) - 24 + 1;

            if (position + sizeLength > data.Length)
            {
                return false;
            }

            long value = first & (0xFF >> sizeLength);
            var allOnes = value == (0xFF >> sizeLength);

            for (var i = 1; i < sizeLength; i++)
            {
                value = (value << 8) | data[position + i];
                allOnes &= data[position + i] == 0xFF;
            }

            position += sizeLength;
            size = allOnes ? -1 : value;

            return true;
        }

        private static ulong ReadUInt(ReadOnlySpan<byte> data)
        {
            ulong value = 0;

            foreach (var b in data[..Math.Min(8, data.Length)])
            {
                value = (value << 8) | b;
            }

            return value;
        }

        #endregion

        #region MP4

        private static readonly HashSet<string> Mp4Containers = new() { "moov", "trak", "mdia", "minf", "stbl" };

        private static readonly HashSet<string> Mp4VideoEntries = new() { "hvc1", "hev1", "dvh1", "dvhe", "avc1", "avc3", "dva1", "dvav", "av01", "dav1", "vp09" };

        private static ColourInfo? ReadMp4(RangeReader reader)
        {
            // Yuqori darajadagi qutilar orasidan moov topiladi (oxirida bo'lishi ham mumkin).
            long position = 0;

            while (position + 8 <= reader.Length)
            {
                if (ReadBoxHeader(reader, position, reader.Length) is not { } box)
                {
                    return null;
                }

                if (box.Type == "moov")
                {
                    return box.Size > MaxBoxSize ? new ColourInfo() : ReadMp4Container(reader, box);
                }

                if (box.Size <= 0)
                {
                    break;
                }

                position += box.Size;
            }

            return new ColourInfo();
        }

        private static ColourInfo? ReadMp4Container(RangeReader reader, Box parent)
        {
            var data = reader.Read(parent.DataOffset, (int)(parent.End - parent.DataOffset));

            if (data == null)
            {
                return null;
            }

            return FindVideoEntry(data, parent.Type) ?? new ColourInfo();
        }

        /// <summary>moov ichidan (xotirada) birinchi video sample entry'ning rang ma'lumoti.</summary>
        private static ColourInfo? FindVideoEntry(ReadOnlySpan<byte> data, string type)
        {
            var offset = 0;

            if (type == "stsd")
            {
                // FullBox (versiya + flaglar) va yozuvlar soni.
                offset = 8;
            }

            while (offset + 8 <= data.Length)
            {
                var size = (long)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                var childType = Encoding.ASCII.GetString(data.Slice(offset + 4, 4));
                var header = 8;

                if (size == 1 && offset + 16 <= data.Length)
                {
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]);
                    header = 16;
                }
                else if (size == 0)
                {
                    size = data.Length - offset;
                }

                if (size < header || offset + size > data.Length)
                {
                    return null;
                }

                var body = data.Slice(offset + header, (int)size - header);

                if (Mp4Containers.Contains(childType) || childType == "stsd")
                {
                    if (FindVideoEntry(body, childType) is { } found)
                    {
                        return found;
                    }
                }
                else if (type == "stsd" && Mp4VideoEntries.Contains(childType))
                {
                    return ReadVideoSampleEntry(body, childType);
                }

                offset += (int)size;
            }

            return null;
        }

        private static ColourInfo ReadVideoSampleEntry(ReadOnlySpan<byte> entry, string type)
        {
            // SampleEntry (8) + VisualSampleEntry maydonlari (70) — keyin ichki qutilar.
            const int VisualSampleEntrySize = 78;

            var info = new ColourInfo { HasDolbyVision = type is "dvh1" or "dvhe" or "dva1" or "dvav" or "dav1" };
            byte[]? hevcConfig = null;
            var offset = VisualSampleEntrySize;

            while (offset + 8 <= entry.Length)
            {
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[offset..]);
                var childType = Encoding.ASCII.GetString(entry.Slice(offset + 4, 4));

                if (size < 8 || offset + size > entry.Length)
                {
                    break;
                }

                var body = entry.Slice(offset + 8, size - 8);

                switch (childType)
                {
                    case "colr" when body.Length >= 10 && Encoding.ASCII.GetString(body[..4]) is "nclx" or "nclc":
                        info.Transfer = BinaryPrimitives.ReadUInt16BigEndian(body[6..]);
                        break;
                    case "dvcC" or "dvvC" or "dvwC":
                        info.HasDolbyVision = true;
                        break;
                    case "hvcC":
                        hevcConfig = body.ToArray();
                        break;
                }

                offset += size;
            }

            if (info.Transfer == null && hevcConfig != null)
            {
                info.Transfer = Hevc.ReadTransferFromConfig(hevcConfig);
            }

            return info;
        }

        private static Box? ReadBoxHeader(RangeReader reader, long position, long limit)
        {
            var header = reader.Read(position, (int)Math.Min(16, limit - position));

            if (header == null || header.Length < 8)
            {
                return null;
            }

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8;

            if (size == 1)
            {
                if (header.Length < 16)
                {
                    return null;
                }

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = limit - position;
            }

            return size < headerSize ? null : new Box(type, position, size, position + headerSize);
        }

        private sealed record Box(string Type, long Offset, long Size, long DataOffset)
        {
            public long End => Offset + Size;
        }

        #endregion

        /// <summary>HEVC parametr to'plamlaridan (SPS → VUI) transfer_characteristics.</summary>
        private static class Hevc
        {
            private const int SpsNalType = 33;

            /// <summary>hvcC (HEVCDecoderConfigurationRecord) ichidagi SPS'dan.</summary>
            public static int? ReadTransferFromConfig(byte[] config)
            {
                if (config.Length < 23)
                {
                    return null;
                }

                try
                {
                    var arrays = config[22];
                    var offset = 23;

                    for (var i = 0; i < arrays && offset + 3 <= config.Length; i++)
                    {
                        var nalType = config[offset] & 0x3F;
                        var count = BinaryPrimitives.ReadUInt16BigEndian(config.AsSpan(offset + 1));
                        offset += 3;

                        for (var j = 0; j < count && offset + 2 <= config.Length; j++)
                        {
                            var length = BinaryPrimitives.ReadUInt16BigEndian(config.AsSpan(offset));
                            offset += 2;

                            if (nalType == SpsNalType && offset + length <= config.Length)
                            {
                                return ReadSpsTransfer(config.AsSpan(offset, length));
                            }

                            offset += length;
                        }
                    }
                }
                catch (IndexOutOfRangeException)
                {
                    // Buzilgan yoki kutilmagan SPS.
                }

                return null;
            }

            /// <summary>H.265 7.3.2.2 bo'yicha VUI'gacha o'qiladi.</summary>
            private static int? ReadSpsTransfer(ReadOnlySpan<byte> nal)
            {
                var bits = new BitReader(RemoveEmulationPrevention(nal[2..]));

                bits.Skip(4);
                var maxSubLayersMinus1 = bits.Read(3);
                bits.Skip(1);
                SkipProfileTierLevel(bits, maxSubLayersMinus1);

                bits.ReadUe();
                var chromaFormat = bits.ReadUe();

                if (chromaFormat == 3)
                {
                    bits.Skip(1);
                }

                bits.ReadUe();
                bits.ReadUe();

                if (bits.ReadFlag())
                {
                    for (var i = 0; i < 4; i++)
                    {
                        bits.ReadUe();
                    }
                }

                bits.ReadUe();
                bits.ReadUe();
                var log2MaxPocLsb = bits.ReadUe() + 4;
                var orderingInfoPresent = bits.ReadFlag();

                for (var i = orderingInfoPresent ? 0 : maxSubLayersMinus1; i <= maxSubLayersMinus1; i++)
                {
                    bits.ReadUe();
                    bits.ReadUe();
                    bits.ReadUe();
                }

                for (var i = 0; i < 6; i++)
                {
                    bits.ReadUe();
                }

                if (bits.ReadFlag() && bits.ReadFlag())
                {
                    SkipScalingListData(bits);
                }

                bits.Skip(2);

                if (bits.ReadFlag())
                {
                    bits.Skip(8);
                    bits.ReadUe();
                    bits.ReadUe();
                    bits.Skip(1);
                }

                SkipShortTermRefPicSets(bits);

                if (bits.ReadFlag())
                {
                    var longTermCount = bits.ReadUe();

                    for (var i = 0; i < longTermCount; i++)
                    {
                        bits.Skip(log2MaxPocLsb + 1);
                    }
                }

                bits.Skip(2);

                if (!bits.ReadFlag())
                {
                    return null;
                }

                // vui_parameters()
                if (bits.ReadFlag() && bits.Read(8) == 255)
                {
                    bits.Skip(32);
                }

                if (bits.ReadFlag())
                {
                    bits.Skip(1);
                }

                if (!bits.ReadFlag())
                {
                    return null;
                }

                bits.Skip(4);

                if (!bits.ReadFlag())
                {
                    return null;
                }

                bits.Skip(8);

                return bits.Read(8);
            }

            private static void SkipProfileTierLevel(BitReader bits, int maxSubLayersMinus1)
            {
                // general_profile_space .. general_level_idc
                bits.Skip(2 + 1 + 5 + 32 + 48 + 8);

                var profilePresent = new bool[maxSubLayersMinus1];
                var levelPresent = new bool[maxSubLayersMinus1];

                for (var i = 0; i < maxSubLayersMinus1; i++)
                {
                    profilePresent[i] = bits.ReadFlag();
                    levelPresent[i] = bits.ReadFlag();
                }

                if (maxSubLayersMinus1 > 0)
                {
                    bits.Skip(2 * (8 - maxSubLayersMinus1));
                }

                for (var i = 0; i < maxSubLayersMinus1; i++)
                {
                    bits.Skip((profilePresent[i] ? 88 : 0) + (levelPresent[i] ? 8 : 0));
                }
            }

            private static void SkipScalingListData(BitReader bits)
            {
                for (var sizeId = 0; sizeId < 4; sizeId++)
                {
                    for (var matrixId = 0; matrixId < 6; matrixId += sizeId == 3 ? 3 : 1)
                    {
                        if (!bits.ReadFlag())
                        {
                            bits.ReadUe();

                            continue;
                        }

                        var coefficients = Math.Min(64, 1 << (4 + (sizeId << 1)));

                        if (sizeId > 1)
                        {
                            bits.ReadUe();
                        }

                        for (var i = 0; i < coefficients; i++)
                        {
                            bits.ReadUe();
                        }
                    }
                }
            }

            private static void SkipShortTermRefPicSets(BitReader bits)
            {
                var count = bits.ReadUe();
                var deltaPocs = new int[count];

                for (var index = 0; index < count; index++)
                {
                    if (index != 0 && bits.ReadFlag())
                    {
                        // inter_ref_pic_set_prediction: oldingi to'plamdan olinadi.
                        bits.Skip(1);
                        bits.ReadUe();

                        var used = 0;

                        for (var j = 0; j <= deltaPocs[index - 1]; j++)
                        {
                            if (bits.ReadFlag() || bits.ReadFlag())
                            {
                                used++;
                            }
                        }

                        deltaPocs[index] = used;
                    }
                    else
                    {
                        var negative = bits.ReadUe();
                        var positive = bits.ReadUe();

                        for (var i = 0; i < negative + positive; i++)
                        {
                            bits.ReadUe();
                            bits.Skip(1);
                        }

                        deltaPocs[index] = negative + positive;
                    }
                }
            }

            private static byte[] RemoveEmulationPrevention(ReadOnlySpan<byte> data)
            {
                var result = new List<byte>(data.Length);
                var zeros = 0;

                foreach (var b in data)
                {
                    if (zeros >= 2 && b == 3)
                    {
                        zeros = 0;

                        continue;
                    }

                    zeros = b == 0 ? zeros + 1 : 0;
                    result.Add(b);
                }

                return result.ToArray();
            }
        }

        /// <summary>Exp-Golomb (ue) qo'llaydigan bit o'quvchi.</summary>
        private sealed class BitReader
        {
            private readonly byte[] _data;
            private int _position;

            public BitReader(byte[] data)
            {
                _data = data;
            }

            public bool ReadFlag()
            {
                return Read(1) == 1;
            }

            public int Read(int count)
            {
                var value = 0;

                for (var i = 0; i < count; i++)
                {
                    value = (value << 1) | ((_data[_position >> 3] >> (7 - (_position & 7))) & 1);
                    _position++;
                }

                return value;
            }

            public void Skip(int count)
            {
                _position += count;
            }

            public int ReadUe()
            {
                var zeros = 0;

                while (Read(1) == 0)
                {
                    if (++zeros > 31)
                    {
                        throw new IndexOutOfRangeException();
                    }
                }

                return (1 << zeros) - 1 + Read(zeros);
            }
        }

        /// <summary>Faylning faqat diskda bor (yuklangan) qismlarini o'qiydi.</summary>
        private sealed class RangeReader
        {
            private readonly Stream _stream;
            private readonly Func<long, long, bool> _isAvailable;

            public RangeReader(Stream stream, Func<long, long, bool> isAvailable)
            {
                _stream = stream;
                _isAvailable = isAvailable;
            }

            public long Length => _stream.Length;

            public byte[]? Read(long offset, int count)
            {
                if (count <= 0 || offset < 0 || offset + count > _stream.Length || !_isAvailable(offset, count))
                {
                    return null;
                }

                var buffer = new byte[count];
                _stream.Position = offset;
                _stream.ReadExactly(buffer);

                return buffer;
            }
        }

        private sealed class ColourInfo
        {
            public int? Transfer { get; set; }

            public bool HasDolbyVision { get; set; }
        }
    }
}
