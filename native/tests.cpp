#include "bridge.cpp"
#include <libtorrent/create_torrent.hpp>
#include <libtorrent/bencode.hpp>
#include <libtorrent/load_torrent.hpp>
#include <thread>
#include <iostream>

int main() {
    auto root=fs::temp_directory_path()/fs::u8path("watchtorrent-loopback-"+std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    fs::create_directories(root/"seed"); fs::create_directories(root/"download");
    try {
        std::string data(128*1024,'\0');
        for(std::size_t i=0;i<data.size();++i)data[i]=char(i%251);
        {std::ofstream out(root/"seed"/"video.bin",std::ios::binary);out.write(data.data(),data.size());}
        lt::create_torrent creator(std::vector<lt::create_file_entry>{{"video.bin",std::int64_t(data.size())}},16*1024);
        lt::set_piece_hashes(creator,(root/"seed").u8string());
        std::vector<char> encoded; lt::bencode(std::back_inserter(encoded),creator.generate());
        auto seed_params=lt::load_torrent_buffer(lt::span<char const>(encoded.data(),encoded.size()));
        lt::settings_pack settings;
        settings.set_str(lt::settings_pack::listen_interfaces,"127.0.0.1:0");
        settings.set_bool(lt::settings_pack::enable_dht,false);
        settings.set_bool(lt::settings_pack::enable_lsd,false);
        settings.set_bool(lt::settings_pack::enable_upnp,false);
        settings.set_bool(lt::settings_pack::enable_natpmp,false);
        {
        lt::session seed(settings);
        auto p=seed_params;p.save_path=(root/"seed").u8string();
        p.flags|=lt::torrent_flags::seed_mode;
        p.flags&=~(lt::torrent_flags::auto_managed|lt::torrent_flags::paused);
        seed.add_torrent(p);
        auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(40);
        while(!seed.listen_port()) {if(std::chrono::steady_clock::now()>deadline)throw std::runtime_error("Seeder listen timeout");std::this_thread::sleep_for(std::chrono::milliseconds(20));}
        auto magnet=lt::make_magnet_uri(seed_params);
        std::string hash;
        {
            context download((root/"cache").u8string(),false);
            download.session.apply_settings(settings);
            auto added=download.request({{"Op","add"},{"Magnet",magnet},{"Directory",(root/"download").u8string()}});
            hash=added.at("Hash");
            download.torrents.at(hash).handle.connect_peer({lt::make_address("127.0.0.1"),static_cast<unsigned short>(seed.listen_port())});
            unsigned char buffer[4096];int result=-1;
            while(result<0) {
                if(std::chrono::steady_clock::now()>deadline)throw std::runtime_error("Loopback torrent download timed out");
                auto snapshot=download.snapshot(hash);
                if(!snapshot.at("Files").empty())result=wt_read(&download,hash.c_str(),0,0,buffer,0,sizeof(buffer));
                if(result==-2)throw std::runtime_error(last_error);
                std::this_thread::sleep_for(std::chrono::milliseconds(25));
            }
            if(result!=sizeof(buffer)||std::memcmp(buffer,data.data(),result))throw std::runtime_error("Verified piece bytes differ");
            download.request({{"Op","pause"},{"Hash",hash},{"Paused",true}});
            download.request({{"Op","save"}});
        }
        {
            context restored((root/"cache").u8string(),false);
            auto snapshot=restored.snapshot(hash);
            if(snapshot.at("State")!=0)throw std::runtime_error("Pause state was not restored");
            restored.request({{"Op","remove"},{"Hash",hash},{"DeleteFiles",false}});
            if(!fs::exists(root/"download"/"video.bin"))throw std::runtime_error("Keep-files removal deleted data");
        }
        }
        std::cout<<"PASS real loopback torrent metadata, piece read, pause/resume persistence and keep-files removal\n";
        // All test-owned paths live in the unique temporary directory above.
        fs::remove_all(root);
        return 0;
    }catch(std::exception const& e){std::cerr<<e.what()<<"\n";return 1;}
}
