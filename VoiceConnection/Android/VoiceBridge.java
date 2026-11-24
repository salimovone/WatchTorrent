package io.watchtorrent.voice;

import android.content.Context;
import io.github.pytgcalls.*;
import io.github.pytgcalls.media.*;
import io.github.pytgcalls.p2p.RTCServer;
import java.util.Arrays;
import java.util.ArrayList;
import java.util.Collections;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicLong;
import java.lang.ref.WeakReference;

/** Small Java boundary; native audio and ICE stay in WebRTC, away from the MAUI UI thread. */
public final class VoiceBridge implements AutoCloseable {
    private final ArrayBlockingQueue<byte[]> events = new ArrayBlockingQueue<>(256);
    private final NTgCalls engine;
    private static final AtomicLong nextCall = new AtomicLong();
    private static final ConcurrentHashMap<Long, WeakReference<VoiceBridge>> sessions = new ConcurrentHashMap<>();
    private static NTgCalls sharedEngine;
    private final long call = nextCall.incrementAndGet();
    private volatile boolean closed, overflow;
    private boolean microphoneConfigured;

    private static synchronized NTgCalls getEngine(Context context) {
        if (sharedEngine != null) return sharedEngine;
        try {
            Class.forName("org.webrtc.ContextUtils").getMethod("initialize", Context.class)
                .invoke(null, context.getApplicationContext());
        } catch (ReflectiveOperationException error) { throw new IllegalStateException(error); }
        NTgCalls instance = new NTgCalls();
        // Native JNI callbacks own global references. Route through weak per-call entries so closed calls
        // cannot retain their bridge, audio buffers or activities until process exit.
        instance.onSignalingData((id, data) -> {
            WeakReference<VoiceBridge> entry = sessions.get(id);
            VoiceBridge bridge = entry == null ? null : entry.get();
            if (bridge == null) return;
            if (data.length > 65536) { bridge.overflow = true; return; }
            byte[] event = new byte[data.length + 1];
            event[0] = 1;
            System.arraycopy(data, 0, event, 1, data.length);
            bridge.offer(event);
        });
        instance.onConnectionChange((id, info) -> {
            WeakReference<VoiceBridge> entry = sessions.get(id);
            VoiceBridge bridge = entry == null ? null : entry.get();
            if (bridge == null) return;
            if (info.kind != ConnectionKind.NORMAL) return;
            byte state;
            switch (info.state) {
                case CONNECTED: state = 2; break;
                case CONNECTING: state = 0; break;
                default: state = 3; break;
            }
            bridge.offer(new byte[] { 2, state });
        });
        sharedEngine = instance;
        return instance;
    }

    public VoiceBridge(Context context, byte[] key, boolean outgoing, String relayHost, int relayPort, String relayUser, String relayPassword, boolean relayOnly) {
        engine = getEngine(context);
        sessions.put(call, new WeakReference<>(this));
        try {
            engine.createP2pCall(call);
            engine.skipExchange(call, key, outgoing);
            MediaDevices devices = NTgCalls.getMediaDevices();
            String speaker = devices.speaker.isEmpty() ? "" : devices.speaker.get(0).metadata;
            engine.setStreamSources(call, StreamMode.PLAYBACK,
                new MediaDescription(new AudioDescription(MediaSource.DEVICE, 48000, 1, speaker, false), null, null, null));
            engine.mute(call);
            ArrayList<RTCServer> servers = new ArrayList<>();
            servers.add(new RTCServer(1, "stun.l.google.com", "", 19302, "", "", false, true, false, null));
            if (!relayHost.isEmpty()) servers.add(new RTCServer(2, relayHost, "", relayPort, relayUser, relayPassword, true, false, false, null));
            engine.connectP2p(call, servers, Collections.singletonList("13.0.0"), !relayOnly, null);
        } catch (RuntimeException | Error error) {
            try { close(); } catch (Throwable ignored) { }
            throw error;
        }
    }

    private void offer(byte[] event) {
        if (!closed && !events.offer(event)) overflow = true;
    }

    public byte[] readEvent() {
        if (overflow) throw new IllegalStateException("VoiceConnection event queue overflow");
        return events.poll();
    }

    public void receive(byte[] data) {
        if (closed) throw new IllegalStateException("VoiceConnection is closed");
        engine.sendSignalingData(call, data);
    }

    public void setMuted(boolean muted) {
        if (closed) throw new IllegalStateException("VoiceConnection is closed");
        if (muted) { engine.mute(call); return; }
        if (!microphoneConfigured) {
            MediaDevices devices = NTgCalls.getMediaDevices();
            if (devices.microphone.isEmpty()) throw new IllegalStateException("No microphone device");
            engine.setStreamSources(call, StreamMode.CAPTURE,
                new MediaDescription(new AudioDescription(MediaSource.DEVICE, 48000, 1,
                    devices.microphone.get(0).metadata, false), null, null, null));
            microphoneConfigured = true;
        }
        engine.unmute(call);
    }

    public long captureTime() { return engine.time(call, StreamMode.CAPTURE); }
    public long playbackTime() { return engine.time(call, StreamMode.PLAYBACK); }

    @Override public void close() {
        if (closed) return;
        closed = true;
        sessions.remove(call);
        events.clear();
        engine.stop(call);
    }
}
