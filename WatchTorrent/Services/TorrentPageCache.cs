using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WatchTorrent.Models;

namespace WatchTorrent.Services;

public sealed class TorrentPageCache(string directory)
{
    private readonly SemaphoreSlim _writes = new SemaphoreSlim(1, 1);

    public async Task<TorrentPageData?> LoadAsync(string infoHash)
    {
        try
        {
            string path = GetPath(infoHash);
            if (!File.Exists(path))
            {
                return null;
            }
            var page = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path), TorrentPageJsonContext.Default.TorrentPageData);
            var detail = page?.Detail;
            return (detail?.Files != null && detail.Tags != null && detail.Comments != null && string.Equals(detail.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase)) ? page : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            Debug.WriteLine("[Torrent pages] Cache unavailable: " + ex.Message);
            return null;
        }
    }

    public async Task SaveAsync(TorrentPageData page)
    {
        string path = GetPath(page.Detail.InfoHash);
        await _writes.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(page, TorrentPageJsonContext.Default.TorrentPageData));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Debug.WriteLine("[Torrent pages] Could not save: " + ex.Message);
        }
        finally
        {
            _writes.Release();
        }
    }

    public async Task RemoveAsync(string infoHash)
    {
        string path = GetPath(infoHash);
        await _writes.WaitAsync();
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Debug.WriteLine("[Torrent pages] Could not remove: " + ex.Message);
        }
        finally
        {
            _writes.Release();
        }
    }

    private string GetPath(string infoHash)
    {
        if (infoHash.Length is not (40 or 64) || !infoHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Invalid torrent hash.", "infoHash");
        }
        return Path.Combine(directory, infoHash.ToUpperInvariant() + ".json");
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(TorrentPageData))]
internal partial class TorrentPageJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
