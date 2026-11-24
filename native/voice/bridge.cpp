#include "bridge.h"
#include <ntgcalls.h>
#include <algorithm>
#include <deque>
#include <memory>
#include <mutex>
#include <vector>
#include <cstdio>
#include <cstdlib>

namespace {
constexpr int64_t Call = 1;
struct Session {
    std::mutex mutex;
    std::deque<std::pair<int, std::vector<uint8_t>>> events;
    ntg_instance *engine = nullptr;
    bool closed = false, failed = false, microphoneConfigured = false;
    ~Session() { if (engine) ntg_instance_destroy(engine); }
    void push(int kind, const uint8_t *bytes, size_t length) noexcept {
        std::lock_guard<std::mutex> lock(mutex);
        if (closed || failed) return;
        try {
            if (events.size() >= 256 || length > 65536) { failed = true; return; }
            events.emplace_back(kind, std::vector<uint8_t>(bytes, bytes + length));
        } catch (...) { failed = true; }
    }
};
bool configureDevice(Session *session, bool capture) {
    ntg_media_devices devices{};
    if (ntg_get_media_devices(&devices) != NTG_OK) return false;
    char empty[] = "";
    auto metadata = capture
        ? (devices.microphone_len ? devices.microphone[0].metadata : empty)
        : (devices.speaker_len ? devices.speaker[0].metadata : empty);
    ntg_audio_description audio{NTG_MEDIA_SOURCE_DEVICE, 48000, 1, metadata, false};
    ntg_media_description media{&audio, nullptr, nullptr, nullptr};
    auto result = ntg_set_stream_sources(session->engine, Call,
        capture ? NTG_STREAM_MODE_CAPTURE : NTG_STREAM_MODE_PLAYBACK, media);
    ntg_media_devices_free(&devices);
    return result == NTG_OK;
}
}

int vc_abi_version(void) { return 2; }
void *vc_create(const uint8_t *key, int length, int outgoing, const char *relay_host, int relay_port, const char *relay_user, const char *relay_password, int relay_only) {
    if (!key || length != 256 || (outgoing != 0 && outgoing != 1)) return nullptr;
    try {
        static std::once_flag logging;
        std::call_once(logging, [] {
            if (std::getenv("VOICECONNECTION_DIAGNOSTICS")) ntg_set_log_callback([](ntg_log_message message, void*) {
                if ((message.level & (NTG_LOG_WARNING | NTG_LOG_ERROR)) && message.message)
                    std::fprintf(stderr, "[VoiceConnection native] %s\n", message.message);
            }, nullptr);
        });
        auto session = std::make_unique<Session>();
        session->engine = ntg_instance_create();
        if (!session->engine) return nullptr;
        if (ntg_on_signaling_data_callback(session->engine,
            [](ntg_instance*, int64_t, const uint8_t *data, size_t size, void *context) {
                static_cast<Session*>(context)->push(1, data, size);
            }, session.get()) != NTG_OK) return nullptr;
        if (ntg_on_connection_change_callback(session->engine,
            [](ntg_instance*, int64_t, ntg_connection_info info, void *context) {
                if (info.kind != NTG_CONNECTION_KIND_NORMAL) return;
                uint8_t state = info.state == NTG_CONNECTION_STATE_CONNECTED ? 2
                    : info.state == NTG_CONNECTION_STATE_CONNECTING ? 0 : 3;
                static_cast<Session*>(context)->push(2, &state, 1);
            }, session.get()) != NTG_OK) return nullptr;
        if (ntg_create_p2p_call(session->engine, Call) != NTG_OK ||
            ntg_skip_exchange(session->engine, Call, key, length, outgoing != 0) != NTG_OK ||
            !configureDevice(session.get(), false)) return nullptr;
        bool changed;
        if (ntg_mute(session->engine, Call, &changed) != NTG_OK) return nullptr;
        char host[] = "stun.l.google.com", empty[] = "";
        ntg_rtc_server servers[] = {
            {1, host, empty, 19302, empty, empty, false, true, false, nullptr, 0},
            {2, const_cast<char*>(relay_host), empty, static_cast<uint16_t>(relay_port), const_cast<char*>(relay_user), const_cast<char*>(relay_password), true, false, false, nullptr, 0}
        };
        const char *version = "13.0.0";
        if (ntg_connect_p2p(session->engine, Call, servers, relay_host && *relay_host ? 2 : 1, &version, 1, relay_only == 0, nullptr) != NTG_OK) return nullptr;
        return session.release();
    } catch (...) { return nullptr; }
}
int vc_receive(void *handle, const uint8_t *data, int length) {
    if (!handle || !data || length <= 0 || length > 65536) return -1;
    return ntg_send_signaling_data(static_cast<Session*>(handle)->engine, Call, data, length);
}
int vc_mute(void *handle, int muted) {
    if (!handle) return -1;
    auto session = static_cast<Session*>(handle);
    if (!muted && !session->microphoneConfigured) {
        if (!configureDevice(session, true)) return -1;
        session->microphoneConfigured = true;
    }
    bool changed;
    return muted ? ntg_mute(session->engine, Call, &changed) : ntg_unmute(session->engine, Call, &changed);
}
int vc_poll(void *handle, int *kind, uint8_t *data, int capacity) {
    if (!handle || !kind || !data || capacity < 0) return -1;
    *kind = 0;
    auto session = static_cast<Session*>(handle);
    std::lock_guard<std::mutex> lock(session->mutex);
    if (session->failed) return -1;
    if (session->events.empty()) return 0;
    auto &event = session->events.front();
    if (event.second.size() > static_cast<size_t>(capacity)) return -1;
    *kind = event.first;
    auto length = static_cast<int>(event.second.size());
    std::copy(event.second.begin(), event.second.end(), data);
    session->events.pop_front();
    return length;
}
void vc_destroy(void *handle) {
    if (!handle) return;
    std::unique_ptr<Session> session(static_cast<Session*>(handle));
    {
        std::lock_guard<std::mutex> lock(session->mutex);
        session->closed = true;
        session->events.clear();
    }
    ntg_stop(session->engine, Call);
}
int64_t vc_time(void *handle, int mode) {
    if (!handle) return -1;
    uint64_t result;
    if (ntg_time(static_cast<Session*>(handle)->engine, Call,
        mode == 0 ? NTG_STREAM_MODE_CAPTURE : NTG_STREAM_MODE_PLAYBACK, &result) != NTG_OK) return -1;
    return static_cast<int64_t>(result);
}
