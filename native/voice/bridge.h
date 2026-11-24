#pragma once
#include <stdint.h>
#ifdef _WIN32
#define WT_TG_API __declspec(dllexport)
#else
#define WT_TG_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
WT_TG_API int vc_abi_version(void);
WT_TG_API void *vc_create(const uint8_t *key, int length, int outgoing, const char *relay_host, int relay_port, const char *relay_user, const char *relay_password, int relay_only);
WT_TG_API int vc_receive(void *session, const uint8_t *data, int length);
WT_TG_API int vc_mute(void *session, int muted);
// kind: 0 empty, 1 encrypted signaling, 2 state byte matching VoiceConnection.CallState.
// Negative result is an error; no C++ exceptions cross this ABI.
WT_TG_API int vc_poll(void *session, int *kind, uint8_t *data, int capacity);
WT_TG_API void vc_destroy(void *session);
WT_TG_API int64_t vc_time(void *session, int mode);
#ifdef __cplusplus
}
#endif
