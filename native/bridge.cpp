#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#endif
#include <libtorrent/session.hpp>
#include <libtorrent/session_params.hpp>
#include <libtorrent/magnet_uri.hpp>
#include <libtorrent/torrent_info.hpp>
#include <libtorrent/torrent_status.hpp>
#include <libtorrent/torrent_flags.hpp>
#include <libtorrent/alert_types.hpp>
#include <libtorrent/read_resume_data.hpp>
#include <libtorrent/write_resume_data.hpp>
#include <libtorrent/version.hpp>
#include <nlohmann/json.hpp>
#include <filesystem>
#include <fstream>
#include <map>
#include <set>
#include <cstring>
#include <cstdlib>

#if defined(_WIN32)
#define WT_EXPORT extern "C" __declspec(dllexport)
#else
#define WT_EXPORT extern "C" __attribute__((visibility("default")))
#endif
namespace lt = libtorrent;
namespace fs = std::filesystem;
using json = nlohmann::json;
static thread_local std::string last_error;
static std::string hash_of(lt::info_hash_t const& hashes) {
    auto bytes = hashes.has_v1() ? hashes.v1.to_string() : hashes.v2.to_string();
    static char const digits[] = "0123456789abcdef";
    std::string result;
    for (unsigned char b : bytes) { result += digits[b >> 4]; result += digits[b & 15]; }
    return result;
}
static void atomic_write(fs::path const& path, std::string const& bytes) {
    auto temp = path; temp += ".tmp";
    { std::ofstream out(temp, std::ios::binary | std::ios::trunc); out.write(bytes.data(), bytes.size());
      if (!out) throw std::runtime_error("Cannot write torrent resume data"); }
#if defined(_WIN32)
    // std::filesystem::rename does not replace files on Windows.
    if (!MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
        throw std::runtime_error("Cannot replace torrent resume data");
#else
    fs::rename(temp, path);
#endif
}
static std::string read_file(fs::path const& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error("Cannot read torrent resume data");
    return {std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>()};
}

struct transfer { lt::torrent_handle handle; std::string magnet; std::string path; int stream_piece=-1; };
struct context {
    fs::path cache;
    lt::session session;
    std::map<std::string, transfer> torrents;
    std::map<std::pair<std::string,int>, std::vector<char>> reads;
    std::set<std::pair<std::string,int>> pending;

    static lt::session_params parameters(bool mobile) {
        lt::settings_pack settings;
        settings.set_str(lt::settings_pack::user_agent, "WatchTorrent/2 libtorrent/" LIBTORRENT_VERSION);
        settings.set_str(lt::settings_pack::listen_interfaces, "0.0.0.0:0,[::]:0");
        settings.set_bool(lt::settings_pack::enable_dht, true);
        settings.set_bool(lt::settings_pack::enable_lsd, true);
        settings.set_bool(lt::settings_pack::enable_upnp, true);
        settings.set_bool(lt::settings_pack::enable_natpmp, true);
        settings.set_int(lt::settings_pack::connections_limit, mobile ? 200 : 400);
        settings.set_int(lt::settings_pack::alert_mask, lt::alert_category::error | lt::alert_category::storage | lt::alert_category::status);
        return lt::session_params(settings);
    }
    context(std::string const& directory, bool mobile) : cache(fs::u8path(directory)), session(parameters(mobile)) {
        fs::create_directories(cache);
        if (!fs::exists(cache / "transfers.json")) return;
        auto records = json::parse(read_file(cache / "transfers.json"));
        for (auto const& record : records) {
            auto p = lt::parse_magnet_uri(record.at("Magnet").get<std::string>());
            auto hash = hash_of(p.info_hashes);
            auto resume = cache / (hash + ".resume");
            if (fs::exists(resume)) {
                lt::error_code ec;
                auto bytes = read_file(resume);
                auto restored = lt::read_resume_data(lt::span<char const>(bytes.data(), bytes.size()), ec);
                bool same = (p.info_hashes.has_v1() && restored.info_hashes.has_v1() && restored.info_hashes.v1 == p.info_hashes.v1)
                    || (p.info_hashes.has_v2() && restored.info_hashes.has_v2() && restored.info_hashes.v2 == p.info_hashes.v2);
                if (!ec && same) p = std::move(restored);
            }
            p.save_path = record.at("Directory").get<std::string>();
            p.flags &= ~lt::torrent_flags::auto_managed;
            if (record.value("Paused", false)) p.flags |= lt::torrent_flags::paused;
            else p.flags &= ~lt::torrent_flags::paused;
            auto h = session.add_torrent(p);
            torrents.emplace(hash, transfer{h, record.at("Magnet"), p.save_path});
        }
    }
    void manifest() {
        json list = json::array();
        for (auto const& [hash, t] : torrents)
            list.push_back({{"Magnet",t.magnet},{"Directory",t.path},
                {"Paused", bool(t.handle.status().flags & lt::torrent_flags::paused)}});
        atomic_write(cache / "transfers.json", list.dump());
    }
    void save(transfer const& t, std::string const& hash) {
        if (!t.handle.torrent_file()) return; // Magnet-only transfers are preserved by the manifest.
        auto p = t.handle.get_resume_data(lt::torrent_handle::save_info_dict);
        auto bytes = lt::write_resume_data_buf(p);
        atomic_write(cache / (hash + ".resume"), std::string(bytes.begin(),bytes.end()));
    }
    void pump() {
        std::vector<lt::alert*> alerts;
        session.pop_alerts(&alerts);
        for (auto alert : alerts) {
            if (auto r = lt::alert_cast<lt::read_piece_alert>(alert)) {
                auto key = std::make_pair(hash_of(r->handle.info_hashes()), int(r->piece));
                pending.erase(key);
                if (!r->error && r->buffer && r->size > 0) {
                    // Bound the cache; the player only needs a small window.
                    if (reads.size() >= 8) reads.erase(reads.begin());
                    reads[key] = std::vector<char>(r->buffer.get(),r->buffer.get()+r->size);
                }
            }
        }
    }
    json snapshot(std::string const& hash) {
        auto const& t = torrents.at(hash);
        auto s = t.handle.status(lt::torrent_handle::query_pieces | lt::torrent_handle::query_name | lt::torrent_handle::query_save_path);
        int state = s.errc ? 2 : (s.flags & lt::torrent_flags::paused) ? 0
            : s.state == lt::torrent_status::downloading_metadata ? 3
            : (s.state == lt::torrent_status::checking_files || s.state == lt::torrent_status::checking_resume_data) ? 6
            : s.is_finished ? 10 : 8;
        json files = json::array(), pieces = json::array();
        auto ti = t.handle.torrent_file();
        if (ti) {
            auto const& fs = ti->layout();
            auto priorities = t.handle.get_file_priorities();
            for (auto i : fs.file_range()) {
                if (fs.pad_file_at(i)) continue;
                auto offset = fs.file_offset(i), length = fs.file_size(i);
                files.push_back({{"Index",int(i)},{"Path",fs.file_path(i)}, {"Length",length},
                    {"Offset",offset},{"Start",offset/ti->piece_length()},
                    {"End",length ? (offset+length-1)/ti->piece_length() : offset/ti->piece_length()},
                    {"Priority",int(static_cast<std::uint8_t>(priorities.at(int(i))))}});
            }
            for (int i=0;i<ti->num_pieces();++i) pieces.push_back(s.pieces.size()>i && s.pieces[lt::piece_index_t(i)]);
        }
        return {{"Hash",hash},{"Name",s.name},{"Magnet",t.magnet},{"Directory",t.path},
            {"State",state},{"Error",s.errc ? s.errc.message() : ""},
            {"Progress",s.total_wanted > 0 ? 100.0*s.total_wanted_done/s.total_wanted : 0.0},
            {"Size",ti ? ti->total_size() : 0},{"PieceLength",ti ? ti->piece_length() : 0},
            {"DownloadRate",s.download_payload_rate},{"UploadRate",s.upload_payload_rate},
            {"Seeds",s.num_seeds},{"Peers",std::max(0,s.num_peers-s.num_seeds)}, {"Files",files},{"Pieces",pieces}};
    }
    json request(json const& cmd) {
        pump();
        auto op = cmd.at("Op").get<std::string>();
        if (op == "list") { json list=json::array(); for (auto const& [hash,t] : torrents) list.push_back(snapshot(hash)); return list; }
        if (op == "save") { for (auto const& [hash,t] : torrents) save(t,hash); manifest(); return nullptr; }
        if (op == "add") {
            auto magnet = cmd.at("Magnet").get<std::string>();
            auto p = lt::parse_magnet_uri(magnet);
            auto hash = hash_of(p.info_hashes);
            if (!torrents.count(hash)) {
                p.save_path = cmd.at("Directory").get<std::string>();
                p.flags &= ~(lt::torrent_flags::auto_managed | lt::torrent_flags::paused);
                auto h = session.add_torrent(p);
                torrents.emplace(hash,transfer{h,magnet,p.save_path});
                manifest();
            }
            return snapshot(hash);
        }
        auto hash=cmd.at("Hash").get<std::string>();
        auto& t=torrents.at(hash);
        if (op=="snapshot") return snapshot(hash);
        if (op=="pause") {
            t.handle.unset_flags(lt::torrent_flags::auto_managed);
            if (cmd.at("Paused").get<bool>()) t.handle.pause(); else {t.handle.clear_error(); t.handle.resume();}
            save(t,hash); manifest(); return nullptr;
        }
        if (op=="remove") {
            auto record=t; torrents.erase(hash);
            try {manifest();} catch (...) {torrents.emplace(hash,record); throw;}
            session.remove_torrent(record.handle,cmd.at("DeleteFiles").get<bool>() ? lt::session::delete_files : lt::remove_flags_t{});
            fs::remove(cache/(hash+".resume"));
            return nullptr;
        }
        if (op=="priority") {t.handle.file_priority(lt::file_index_t(cmd.at("Index").get<int>()),lt::download_priority_t(static_cast<std::uint8_t>(cmd.at("Priority").get<int>()))); return nullptr;}
        if (op=="close_stream") { t.handle.clear_piece_deadlines(); t.stream_piece=-1; reads.clear(); pending.clear(); return nullptr; }
        throw std::runtime_error("Unknown libtorrent operation");
    }
};

WT_EXPORT char const* wt_last_error() {return last_error.c_str();}
WT_EXPORT void* wt_create(char const* directory, int mobile) {
    try {return new context(directory,mobile!=0);} catch(std::exception const& e) {last_error=e.what();return nullptr;}
}
WT_EXPORT void wt_destroy(void* ptr) {try {delete static_cast<context*>(ptr);} catch (...) {}}
WT_EXPORT void wt_string_free(void* ptr) {std::free(ptr);}
WT_EXPORT char* wt_request(void* ptr, char const* command) {
    try {
        auto result=static_cast<context*>(ptr)->request(json::parse(command)).dump();
        auto memory=static_cast<char*>(std::malloc(result.size()+1));
        if (!memory) throw std::bad_alloc();
        std::memcpy(memory,result.c_str(),result.size()+1);return memory;
    } catch (std::exception const& e) {last_error=e.what(); return nullptr;}
}
// -1 means pending; -2 means an error. Bytes come from read_piece_alert, never sparse disk files.
WT_EXPORT int wt_read(void* ptr,char const* hash,int file,std::int64_t position,unsigned char* buffer,int offset,int count) {
    try {
        auto& ctx=*static_cast<context*>(ptr); ctx.pump();
        auto& t=ctx.torrents.at(hash); auto ti=t.handle.torrent_file();
        if (!ti) return -1;
        auto index=lt::file_index_t(file); auto const& fs=ti->layout();
        if (file<0 || file>=fs.num_files() || position<0 || position>fs.file_size(index) || offset<0 || count<0)
            throw std::runtime_error("Invalid torrent read range");
        if (position==fs.file_size(index) || count==0) return 0;
        auto absolute=fs.file_offset(index)+position;
        auto piece=lt::piece_index_t(int(absolute/ti->piece_length()));
        auto key=std::make_pair(std::string(hash),int(piece));
        auto s=t.handle.status();
        if (s.errc) throw std::runtime_error(s.errc.message());
        // Re-prioritize a bounded window on every read, including after a seek.
        if (t.stream_piece!=int(piece)) {
            t.handle.clear_piece_deadlines(); t.stream_piece=int(piece);
            int end=int((fs.file_offset(index)+fs.file_size(index)-1)/ti->piece_length());
            for (int i=int(piece);i<=std::min(end,int(piece)+15);++i)
                if (!t.handle.have_piece(lt::piece_index_t(i))) t.handle.set_piece_deadline(lt::piece_index_t(i),(i-int(piece))*150);
        }
        auto ready=ctx.reads.find(key);
        if (ready!=ctx.reads.end()) {
            int start=int(absolute%ti->piece_length());
            int n=int(std::min<std::int64_t>({count,std::int64_t(ready->second.size())-start,fs.file_size(index)-position}));
            if (n<=0) throw std::runtime_error("Invalid piece data");
            std::memcpy(buffer+offset,ready->second.data()+start,n);return n;
        }
        if (t.handle.have_piece(piece) && ctx.pending.insert(key).second) t.handle.read_piece(piece);
        return -1;
    } catch(std::exception const& e) {last_error=e.what();return -2;}
}
