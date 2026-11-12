"use strict";
const el = id => document.getElementById(id);
const voice = document.documentElement.dataset.mode === "voice";
const networkHelp = "Ikkala telefonni bir Wi‑Fi’ga ulang va qayta boshlang. Operator NAT’i, firewall yoki UDP cheklovi sabab bo‘lishi mumkin.";
const gatheringMs = 30000, connectionMs = 45000, pongMs = 8000;
let active, phase, events, supported;
let incomingInvite;

function importInvite(file, automatic = false) {
    if (!voice || !supported) return;
    if (file?.format !== "watchtorrent-voice-1" || !/^[a-f0-9]{32}$/.test(file.callId)
        || typeof file.sdp !== "string" || !/^m=audio [1-9]\d* /m.test(file.sdp)) {
        status("Ovozli aloqa faylini o‘qib bo‘lmadi. Yuboruvchidan yangi fayl so‘rang.");
    } else if (automatic && file.type === "answer" && phase === "answer" && active?.callId === file.callId) {
        el("input").value = JSON.stringify(file);
        signal("answer");
    } else if (file.type === "offer" && phase === "start") {
        if (!/^[A-Za-z0-9+/]{43}=$/.test(file.replyKey)) {
            status("Bu taklif eskirgan. Suhbatdoshingizdan yangi taklif yuborishini so‘rang.");
            return;
        }
        incomingInvite = file;
        controls();
        status("Ovozli suhbatga taklif keldi. Ulanish uchun taklifni qabul qiling.");
    } else {
        status(file.type === "answer" ? "Suhbatdoshingiz yuborgan taklif faylini oching."
            : "Avval joriy suhbatni tugating, keyin yangi taklifni oching.");
    }
}

function shareInvite() {
    if (active && el("output").value) location.href = "https://voice.watchtorrent.invalid/share";
}

function outgoingInvite() {
    const description = JSON.parse(el("output").value);
    return {format: "watchtorrent-voice-1", callId: active.callId,
        replyKey: description.type === "offer" ? active.replyKey : undefined, ...description};
}

function autoReplyStatus(id, delivered) {
    if (active?.callId !== id || phase !== "confirm") return;
    if (delivered) startConnecting(active);
    else fail(active, "Ulanib bo‘lmadi. Yangi taklif bilan qayta urinib ko‘ring.");
}

function log(message) {
    events.push(new Date().toLocaleTimeString() + "  " + message);
    events = events.slice(-24);
    el("log").textContent = events.join("\n");
}

function status(message) {
    el("status").textContent = message;
    log(message);
}

function controls() {
    const waiting = phase === "answer";
    el("offer").hidden = !supported || phase !== "start";
    if (voice) {
        const interrupted = active && [active.pc.connectionState, active.pc.iceConnectionState].includes("disconnected");
        const connected = active?.channel?.readyState === "open" && !interrupted;
        const waitingForPeer = !["start", "failed"].includes(phase) && !connected;
        el("indicator").dataset.state = connected ? "on" : waitingForPeer ? "waiting" : "off";
        el("indicator").textContent = connected ? "Ulangan" : interrupted ? "Qayta ulanmoqda"
            : waiting ? "Taklif qabul qilinishi kutilmoqda" : waitingForPeer ? "Ulanmoqda" : "Ulanmagan";
        document.title = connected ? "Ulangan" : "Ovozli aloqa";
        if (incomingInvite) el("offer").hidden = true;
        el("accept").hidden = !incomingInvite || phase !== "start";
        el("pick").hidden = !supported || phase !== "start" || !!incomingInvite;
        el("signaling").hidden = phase === "open" || phase === "failed";
        el("reset").hidden = phase === "start";
        el("reset").textContent = phase === "failed" ? "Qayta boshlash" : "Suhbatni tugatish";
        const mic = active?.mic?.getAudioTracks()[0];
        el("mute").hidden = !connected;
        el("mute").disabled = !!active?.changingMic;
        const transmitting = mic?.readyState === "live" && mic === active?.audioSender?.track && mic.enabled;
        el("mute").setAttribute("aria-checked", String(transmitting));
        el("mute").textContent = transmitting ? "Mening mikrofonim · Yoqilgan" : "Mening mikrofonim · O‘chiq";
    } else {
        el("receive").hidden = !supported || (!waiting && phase !== "start");
        el("prompt").textContent = waiting ? "A telefon · B yuborgan answer matnini joylang." : "B telefon · A yuborgan offer matnini joylang.";
        el("apply").textContent = waiting ? "Answer qo‘llash" : "Offer qabul qilish";
        el("share").hidden = !el("output").value || phase === "failed";
        el("confirm").hidden = phase !== "confirm";
        el("ping").disabled = active?.channel?.readyState !== "open";
    }
}

function diagnostics(s) {
    const p = s?.pc;
    el("states").textContent = [
        "signalingState: " + (p?.signalingState ?? "—"),
        "iceGatheringState: " + (p?.iceGatheringState ?? "—"),
        "iceConnectionState: " + (p?.iceConnectionState ?? "—"),
        "connectionState: " + (p?.connectionState ?? "—"),
        "DataChannel: " + (s?.channel?.readyState ?? "—")
    ].join("\n");
    const counts = Object.entries(s?.counts ?? {}).map(([type, n]) => type + " × " + n);
    el("candidates").textContent = "Lokal candidate turlari: " + (counts.join(", ") || "yig‘ilmagan");
    controls();
}

async function selectedPair(s) {
    if (!s || s.readingStats) return;
    s.readingStats = true;
    let detail = "aniqlanmadi";
    try {
        const stats = await s.pc.getStats();
        for (const transport of stats.values()) {
            if (transport.type !== "transport" || !transport.selectedCandidatePairId) continue;
            const pair = stats.get(transport.selectedCandidatePairId);
            const local = stats.get(pair?.localCandidateId), remote = stats.get(pair?.remoteCandidateId);
            if (local && remote) {
                detail = "lokal " + local.candidateType + "/" + local.protocol
                    + " ↔ masofaviy " + remote.candidateType + "/" + remote.protocol;
                break;
            }
        }
    } catch { /* Older WebViews may not expose transport statistics. */ }
    s.readingStats = false;
    if (active === s) el("pair").textContent = "Tanlangan candidate juftligi: " + detail;
}

function stop() {
    const s = active;
    active = null;
    if (!s) return;
    if (voice && s.replyKey) location.href = "https://voice.watchtorrent.invalid/stop-reply/" + s.callId;
    clearTimeout(s.connectionTimer);
    clearInterval(s.statsTimer);
    s.cancelGather?.();
    for (const ping of s.pings.values()) clearTimeout(ping.timer);
    s.pings.clear();
    const p = s.pc;
    p.ontrack = null;
    if (voice) {
        for (const track of s.mic?.getTracks() ?? []) track.stop();
        el("remoteAudio").srcObject = null;
        el("listen").hidden = true;
    }
    p.onicecandidate = p.ondatachannel = p.onsignalingstatechange = p.onicegatheringstatechange = null;
    p.onconnectionstatechange = p.oniceconnectionstatechange = null;
    if (s.channel) {
        s.channel.onopen = s.channel.onclose = s.channel.onerror = s.channel.onmessage = null;
        s.channel.close();
    }
    p.close();
}

function reset() {
    stop();
    incomingInvite = null;
    events = [];
    phase = "start";
    for (const id of ["input", "output"]) el(id).value = "";
    if (!voice) el("copyHint").textContent = "";
    el("pair").textContent = "Tanlangan candidate juftligi: aniqlanmadi";
    const missing = typeof window.RTCPeerConnection !== "function" ? "RTCPeerConnection"
        : typeof RTCPeerConnection.prototype.createDataChannel !== "function" ? "createDataChannel"
        : voice && !navigator.mediaDevices?.getUserMedia ? "Mikrofon (getUserMedia)" : "";
    supported = !missing;
    diagnostics(null);
    status(missing ? missing + " API mavjud emas. Play Market → Android System WebView sahifasida versiyani tekshiring, yangilang va ilovani qayta oching."
        : voice ? "Taklif yuboring yoki suhbatdoshingizdan kelgan taklif faylini oching."
        : "Bir telefonda offer yarating. Ikkinchisida shu offer’ni qabul qiling.");
}

function fail(s, message) {
    if (s !== active) return;
    diagnostics(s); // Preserve the states at the point of failure.
    stop();
    phase = "failed";
    controls();
    status(message);
}

function armConnection(s) {
    if (s.connectionTimer) return;
    s.connectionTimer = setTimeout(() => fail(s, "Direct ulanishni kutish vaqti tugadi. " + networkHelp), connectionMs);
}

function connectionChanged(s) {
    if (s !== active) return;
    const p = s.pc;
    diagnostics(s);
    log("ICE: " + p.iceConnectionState + " · ulanish: " + p.connectionState);
    selectedPair(s);
    if (p.iceConnectionState === "failed" || p.connectionState === "failed") {
        fail(s, (p.iceConnectionState === "failed" ? "ICE ulanishi muvaffaqiyatsiz. " : "Peer ulanishi muvaffaqiyatsiz. ") + networkHelp);
    } else if (s.opened) {
        if ([p.iceConnectionState, p.connectionState].includes("disconnected")) {
            status("Aloqa vaqtincha uzildi. Tiklanishi kutilmoqda.");
            armConnection(s);
        } else if (["connected", "completed"].includes(p.iceConnectionState) && p.connectionState !== "connecting") {
            if (s.connectionTimer) status(voice ? "Ovozli aloqa tiklandi." : "Aloqa tiklandi. Ping yuboring.");
            clearTimeout(s.connectionTimer);
            s.connectionTimer = null;
        }
    }
}

function attachChannel(s, ch) {
    s.channel = ch;
    function opened() {
        if (active !== s || s.opened) return;
        s.opened = true;
        clearTimeout(s.connectionTimer);
        s.connectionTimer = null;
        phase = "open";
        diagnostics(s);
        status(voice ? "Gapirish uchun mikrofoningizni yoqing." : "Kanal ochildi. Direct ulanishni tekshirish uchun Ping yuboring.");
        selectedPair(s);
        s.statsTimer = setInterval(() => selectedPair(s), 4000);
    }
    ch.onopen = opened;
    ch.onclose = () => fail(s, "Ma’lumot kanali yopildi. " + networkHelp);
    ch.onerror = () => fail(s, "Ma’lumot kanalida xato. " + networkHelp);
    ch.onmessage = event => {
        if (active !== s || ch.readyState !== "open") return;
        let msg;
        try { msg = JSON.parse(event.data); } catch { return; }
        if (voice && msg?.type === "hangup") {
            reset();
            status("Suhbatdosh aloqani tugatdi. Yangi suhbat boshlashingiz mumkin.");
            return;
        }
        if (!msg || !Number.isSafeInteger(msg.id)) return;
        if (msg.type === "ping") {
            try {
                ch.send(JSON.stringify({ type: "pong", id: msg.id }));
                log("Ping #" + msg.id + " olindi, pong yuborildi.");
            } catch { fail(s, "Pong yuborishda kanal xatosi. " + networkHelp); }
        } else if (msg.type === "pong" && s.pings.has(msg.id)) {
            const ping = s.pings.get(msg.id);
            clearTimeout(ping.timer);
            s.pings.delete(msg.id);
            status("Pong #" + msg.id + " olindi · RTT " + (performance.now() - ping.start).toFixed(1) + " ms");
        }
    };
    diagnostics(s);
    if (ch.readyState === "open") opened();
}

function newSession() {
    const pc = new RTCPeerConnection({ iceServers: [{ urls: "stun:stun.l.google.com:19302" }] });
    const s = active = { pc, counts: {}, pings: new Map(), nextId: 0 };
    pc.onicecandidate = ({ candidate }) => {
        if (!candidate || !candidate.candidate) return;
        const type = candidate.type || candidate.candidate.match(/\btyp (host|srflx|prflx|relay)\b/)?.[1] || "noma’lum";
        s.counts[type] = (s.counts[type] || 0) + 1;
        diagnostics(s);
    };
    pc.oniceconnectionstatechange = pc.onconnectionstatechange = () => connectionChanged(s);
    pc.onsignalingstatechange = pc.onicegatheringstatechange = () => {
        diagnostics(s);
        log("Signaling: " + pc.signalingState + " · ICE yig‘ish: " + pc.iceGatheringState);
    };
    diagnostics(s);
    return s;
}

function gather(s) {
    return new Promise((resolve, reject) => {
        let settleTimer;
        function finish(error) {
            clearTimeout(timer);
            clearTimeout(settleTimer);
            s.pc.removeEventListener("icegatheringstatechange", check);
            s.pc.removeEventListener("icecandidate", candidate);
            s.cancelGather = null;
            if (error) reject(error);
            else resolve();
        }
        function check() {
            if (s.pc.iceGatheringState === "complete") finish();
        }
        function candidate() {
            // A voice invitation can use the gathered routes even if another interface is slow.
            if (voice && / typ srflx\b/.test(s.pc.localDescription?.sdp ?? "")) {
                clearTimeout(settleTimer);
                settleTimer = setTimeout(() => finish(), 1500);
            }
        }
        const timer = setTimeout(() => {
            if (voice && /^a=candidate:/m.test(s.pc.localDescription?.sdp ?? "")) finish();
            else finish(new Error("gather-timeout"));
        }, gatheringMs);
        s.cancelGather = () => finish(new Error("cancelled"));
        s.pc.addEventListener("icegatheringstatechange", check);
        s.pc.addEventListener("icecandidate", candidate);
        candidate();
        check();
    });
}

function readDescription(expected) {
    let value;
    try { value = JSON.parse(el("input").value); }
    catch { status("JSON buzilgan. To‘liq matnni qaytadan joylang."); return; }
    if (value?.type !== expected) {
        status("Signaling turi noto‘g‘ri. Bu bosqichda " + expected + " kerak.");
        return;
    }
    if (typeof value.sdp !== "string" || !value.sdp.trim()) {
        status("SDP matni bo‘sh yoki noto‘g‘ri. To‘liq " + expected + " matnini joylang.");
        return;
    }
    if (voice && !/^m=audio [1-9]\d* /m.test(value.sdp)) {
        status("Ovozli kanal topilmadi. Ikkala telefonda ham Ovozli aloqa sahifasini oching.");
        return;
    }
    return { type: expected, sdp: value.sdp };
}

async function playRemote(s) {
    try {
        await el("remoteAudio").play();
        if (active === s) el("listen").hidden = true;
    } catch {
        if (active === s) el("listen").hidden = false;
    }
}

async function toggleMicrophone() {
    const s = active;
    if (!s?.audioSender || s.changingMic || s.channel?.readyState !== "open") return;
    s.changingMic = true;
    controls();
    let stream;
    try {
        if (s.mic) {
            s.mic.getTracks().forEach(track => track.stop());
            s.mic = null;
            await s.audioSender.replaceTrack(null);
        } else {
            stream = await navigator.mediaDevices.getUserMedia({
                audio: { echoCancellation: true, noiseSuppression: true }, video: false
            });
            if (s !== active) return;
            await s.audioSender.replaceTrack(stream.getAudioTracks()[0]);
            if (s !== active) return;
            s.mic = stream;
            stream = null;
        }
        if (s === active) status(s.mic ? "Suhbatdoshingiz sizni eshita oladi." : "Gapirish uchun mikrofoningizni yoqing.");
    } catch {
        if (s === active) status("Mikrofonni yoqib bo‘lmadi. Mikrofon ruxsatini tekshiring.");
    } finally {
        stream?.getTracks().forEach(track => track.stop());
        s.changingMic = false;
        if (s === active) controls();
    }
}

async function signal(action) {
    const remote = action === "create" ? null : readDescription(action);
    if (action !== "create" && !remote) return;
    let s = active, step = "PeerConnection yaratish";
    phase = "busy";
    controls();
    try {
        if (action !== "answer") s = newSession();
        if (voice && action !== "answer") {
            s.callId = action === "offer" && incomingInvite?.sdp === remote.sdp ? incomingInvite.callId
                : Array.from(crypto.getRandomValues(new Uint8Array(16)), n => n.toString(16).padStart(2, "0")).join("");
            s.replyKey = action === "create" ? btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(32))))
                : incomingInvite?.sdp === remote.sdp && /^[A-Za-z0-9+/]{43}=$/.test(incomingInvite.replyKey) ? incomingInvite.replyKey : null;
            incomingInvite = null;
        }
        const pc = s.pc;
        if (voice && action !== "answer") {
            pc.ontrack = ({ track }) => {
                if (s !== active || track.kind !== "audio") return;
                el("remoteAudio").srcObject = new MediaStream([track]);
                playRemote(s);
            };
            if (action === "create")
                s.audioSender = pc.addTransceiver("audio", { direction: "sendrecv" }).sender;
        }
        if (action === "create") {
            step = "DataChannel yaratish";
            attachChannel(s, pc.createDataChannel(voice ? "voice-control" : "direct-test"));
        } else {
            if (action === "offer") pc.ondatachannel = event => attachChannel(s, event.channel);
            step = remote.type + " SDP’sini qo‘llash";
            await pc.setRemoteDescription(remote);
            if (active !== s) return;
            if (voice && action === "offer") {
                const audio = pc.getTransceivers().find(t => t.receiver.track.kind === "audio");
                audio.direction = "sendrecv";
                s.audioSender = audio.sender;
            }
            if (action === "answer") {
                if (!s.opened) startConnecting(s);
                return;
            }
        }
        step = action === "create" ? "Offer yaratish" : "Answer yaratish";
        const description = action === "create" ? await pc.createOffer() : await pc.createAnswer();
        if (active !== s) return;
        step = "Lokal SDP’ni qo‘llash";
        await pc.setLocalDescription(description);
        if (active !== s) return;
        status(voice ? "Suhbatga ulanish tayyorlanmoqda…" : "ICE candidate’lar yig‘ilmoqda…");
        await gather(s);
        if (active !== s) return;
        const { type, sdp } = pc.localDescription;
        el("output").value = JSON.stringify({ type, sdp });
        if (!voice) el("outputLabel").textContent = type + " · ikkinchi telefonga yuboring";
        el("input").value = "";
        phase = action === "create" ? "answer" : "confirm";
        if (voice) {
            status(action === "create" ? "Taklif faylini yuboring. Suhbatdoshingiz qabul qilganda aloqa avtomatik boshlanadi."
                : "Taklif qabul qilindi. Ulanish o‘rnatilmoqda…");
            controls();
            if (action === "offer") location.href = "https://voice.watchtorrent.invalid/auto-reply";
            else shareInvite();
            return;
        }
        status(action === "create" ? "Offer’ni B ga yuboring. Kelgan answer’ni shu yerga joylang."
            : "Answer’ni A ga yuboring. A uni qo‘llagach, pastdagi tugma bilan tasdiqlang.");
        controls();
    } catch (error) {
        if (s !== active) return;
        if (step === "Mikrofonni ochish") {
            fail(s, error.name === "NotAllowedError"
                ? "Mikrofonga ruxsat berilmadi. Android sozlamalarida ilovaning mikrofon ruxsatini yoqing va qayta boshlang."
                : "Mikrofon ochilmadi (" + error.name + "). Mikrofon bandligini tekshirib, qayta boshlang.");
            return;
        }
        const retry = step === "PeerConnection yaratish" || step === "DataChannel yaratish"
            ? "Play Market → Android System WebView sahifasida versiyani tekshiring, yangilang va ilovani qayta oching."
            : "Qayta boshlang.";
        fail(s, error.message === "gather-timeout" ? "ICE gathering timeout. Candidate yig‘ish vaqti tugadi. " + networkHelp
            : step + " bosqichida xato (" + error.name + "). " + retry);
    }
}

function startConnecting(s) {
    phase = "connecting";
    controls();
    status(voice ? "Suhbatdoshingiz bilan aloqa o‘rnatilmoqda…" : "Direct yo‘l va ma’lumot kanali ochilishi kutilmoqda…");
    armConnection(s);
}

el("offer").onclick = () => signal("create");
if (!voice) {
    el("apply").onclick = () => signal(phase === "answer" ? "answer" : "offer");
    el("confirm").onclick = () => startConnecting(active);
}
function endCall() {
    if (voice && active?.channel?.readyState === "open") {
        try { active.channel.send(JSON.stringify({ type: "hangup" })); } catch { }
    }
    reset();
}
el("reset").onclick = endCall;
if (voice) {
    el("pick").onclick = () => { location.href = "https://voice.watchtorrent.invalid/pick"; };
    el("accept").onclick = () => {
        el("input").value = JSON.stringify(incomingInvite);
        signal("offer");
    };
    el("mute").onclick = toggleMicrophone;
    el("listen").onclick = () => playRemote(active);
}
if (!voice) el("copy").onclick = async () => {
    const output = el("output"), s = active;
    output.focus();
    output.select();
    try {
        await navigator.clipboard.writeText(output.value);
        if (active === s) el("copyHint").textContent = "Nusxalandi.";
    } catch {
        if (active === s) el("copyHint").textContent = "Matnni bosib turing va «Nusxalash»ni tanlang.";
    }
};
if (!voice) el("ping").onclick = () => {
    const s = active;
    if (s?.channel?.readyState !== "open") return;
    const id = ++s.nextId;
    const start = performance.now();
    try { s.channel.send(JSON.stringify({ type: "ping", id })); }
    catch { fail(s, "Ping yuborishda kanal xatosi. " + networkHelp); return; }
    const timer = setTimeout(() => {
        s.pings.delete(id);
        status("Pong timeout · ping #" + id + " uchun javob kelmadi. Ping’ni takrorlang.");
    }, pongMs);
    s.pings.set(id, { start, timer });
    status("Ping #" + id + " yuborildi. Pong kutilmoqda…");
};
window.addEventListener("pagehide", voice ? endCall : stop);
window.addEventListener("pageshow", event => {
    if (event.persisted) reset();
});
reset();
