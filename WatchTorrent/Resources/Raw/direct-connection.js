"use strict";
const el = id => document.getElementById(id);
const networkHelp = "Ikkala telefonni bir Wi‑Fi’ga ulang va qayta boshlang. Operator NAT’i, firewall yoki UDP cheklovi sabab bo‘lishi mumkin.";
const gatheringMs = 30000, connectionMs = 45000, pongMs = 8000;
let active, phase, events, supported;

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
    el("receive").hidden = !supported || (!waiting && phase !== "start");
    el("prompt").textContent = waiting ? "A telefon · B yuborgan answer matnini joylang." : "B telefon · A yuborgan offer matnini joylang.";
    el("apply").textContent = waiting ? "Answer qo‘llash" : "Offer qabul qilish";
    el("share").hidden = !el("output").value || phase === "failed";
    el("confirm").hidden = phase !== "confirm";
    el("ping").disabled = active?.channel?.readyState !== "open";
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
    clearTimeout(s.connectionTimer);
    clearInterval(s.statsTimer);
    s.cancelGather?.();
    for (const ping of s.pings.values()) clearTimeout(ping.timer);
    s.pings.clear();
    const p = s.pc;
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
    events = [];
    phase = "start";
    for (const id of ["input", "output"]) el(id).value = "";
    el("copyHint").textContent = "";
    el("pair").textContent = "Tanlangan candidate juftligi: aniqlanmadi";
    const missing = typeof window.RTCPeerConnection !== "function" ? "RTCPeerConnection"
        : typeof RTCPeerConnection.prototype.createDataChannel !== "function" ? "createDataChannel" : "";
    supported = !missing;
    diagnostics(null);
    status(missing ? missing + " API mavjud emas. Play Market → Android System WebView sahifasida versiyani tekshiring, yangilang va ilovani qayta oching."
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
            if (s.connectionTimer) status("Aloqa tiklandi. Ping yuboring.");
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
        status("Kanal ochildi. Direct ulanishni tekshirish uchun Ping yuboring.");
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
        function finish(error) {
            clearTimeout(timer);
            s.pc.removeEventListener("icegatheringstatechange", check);
            s.cancelGather = null;
            if (error) reject(error);
            else resolve();
        }
        function check() {
            if (s.pc.iceGatheringState === "complete") finish();
        }
        const timer = setTimeout(() => finish(new Error("gather-timeout")), gatheringMs);
        s.cancelGather = () => finish(new Error("cancelled"));
        s.pc.addEventListener("icegatheringstatechange", check);
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
    return { type: expected, sdp: value.sdp };
}

async function signal(action) {
    const remote = action === "create" ? null : readDescription(action);
    if (action !== "create" && !remote) return;
    let s = active, step = "PeerConnection yaratish";
    phase = "busy";
    controls();
    try {
        if (action !== "answer") s = newSession();
        const pc = s.pc;
        if (action === "create") {
            step = "DataChannel yaratish";
            attachChannel(s, pc.createDataChannel("direct-test"));
        } else {
            if (action === "offer") pc.ondatachannel = event => attachChannel(s, event.channel);
            step = remote.type + " SDP’sini qo‘llash";
            await pc.setRemoteDescription(remote);
            if (active !== s) return;
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
        status("ICE candidate’lar yig‘ilmoqda…");
        await gather(s);
        if (active !== s) return;
        const { type, sdp } = pc.localDescription;
        el("output").value = JSON.stringify({ type, sdp });
        el("outputLabel").textContent = type + " · ikkinchi telefonga yuboring";
        el("input").value = "";
        phase = action === "create" ? "answer" : "confirm";
        status(action === "create" ? "Offer’ni B ga yuboring. Kelgan answer’ni shu yerga joylang."
            : "Answer’ni A ga yuboring. A uni qo‘llagach, pastdagi tugma bilan tasdiqlang.");
        controls();
    } catch (error) {
        if (s !== active) return;
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
    status("Direct yo‘l va ma’lumot kanali ochilishi kutilmoqda…");
    armConnection(s);
}

el("offer").onclick = () => signal("create");
el("apply").onclick = () => signal(phase === "answer" ? "answer" : "offer");
el("confirm").onclick = () => startConnecting(active);
el("reset").onclick = reset;
el("copy").onclick = async () => {
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
el("ping").onclick = () => {
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
window.addEventListener("pagehide", stop);
window.addEventListener("pageshow", event => {
    if (event.persisted) reset();
});
reset();
