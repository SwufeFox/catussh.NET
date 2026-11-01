(() => {
  const encoder = new TextEncoder();
  const decoder = new TextDecoder();
  const channels = new Map();
  const pendingCandidates = [];
  let peer = null;
  let session = null;
  let handlers = {};
  let controlHeartbeat = null;
  let terminalSessionID = 1;
  let requestID = 1;

  function varint(value) {
    let n = BigInt(value);
    const bytes = [];
    while (n >= 0x80n) { bytes.push(Number((n & 0x7fn) | 0x80n)); n >>= 7n; }
    bytes.push(Number(n));
    return bytes;
  }

  function varintField(field, value) { return [...varint(field << 3), ...varint(value)]; }
  function bytesField(field, value) {
    const bytes = value instanceof Uint8Array ? value : encoder.encode(value);
    return [...varint((field << 3) | 2), ...varint(bytes.length), ...bytes];
  }
  function messageField(field, value) { return bytesField(field, new Uint8Array(value)); }

  function encodeEnvelope(sequence, field, body) {
    const bytes = [];
    if (sequence) bytes.push(...varintField(1, sequence));
    bytes.push(...varintField(2, Date.now()));
    bytes.push(...messageField(field, body));
    return new Uint8Array(bytes);
  }

  function simpleAction(action, args = "", flags = []) {
    const body = [...varintField(1, action)];
    if (args) body.push(...bytesField(2, args));
    if (flags.length) {
      const encoded = [];
      for (const [field, value] of flags) encoded.push(...varintField(field, value));
      body.push(...messageField(4, encoded));
    }
    return body;
  }

  function parseFields(bytes) {
    let offset = 0;
    const fields = [];
    const readVarint = () => {
      let value = 0n;
      let shift = 0n;
      while (offset < bytes.length && shift <= 63n) {
        const b = bytes[offset++];
        value |= BigInt(b & 0x7f) << shift;
        if (!(b & 0x80)) return value;
        shift += 7n;
      }
      throw new Error("Malformed UU protobuf message.");
    };
    while (offset < bytes.length) {
      const key = Number(readVarint());
      const number = key >>> 3;
      const type = key & 7;
      if (type === 0) fields.push({ number, value: readVarint() });
      else if (type === 2) {
        const length = Number(readVarint());
        if (length > bytes.length - offset) throw new Error("Truncated UU protobuf message.");
        fields.push({ number, value: bytes.subarray(offset, offset + length) });
        offset += length;
      } else if (type === 1) offset += 8;
      else if (type === 5) offset += 4;
      else throw new Error("Unsupported UU protobuf wire type.");
      if (offset > bytes.length) throw new Error("Truncated UU protobuf message.");
    }
    return fields;
  }

  function sendControl(data) {
    const channel = channels.get("CONTROL_DATA_CHANNEL");
    if (channel?.readyState === "open") channel.send(data);
  }

  function sendHeartbeat() {
    sendControl(encodeEnvelope(0, 3, simpleAction(0, "", [[1, 2], [2, 2], [3, 1], [4, 2], [6, 2], [7, 2], [8, 3], [9, 1]])));
  }

  function sendVideoRequest() {
    const id = requestID++;
    const header = varintField(1, id);
    const rpc = [...messageField(1, header), ...messageField(15, [])];
    sendControl(encodeEnvelope(id, 21, rpc));
  }

  function replyToEcho(data) {
    const root = parseFields(new Uint8Array(data));
    const sequence = Number(root.find(field => field.number === 1)?.value ?? 0n);
    const actionField = root.find(field => field.number === 3)?.value;
    if (!actionField) return;
    const action = parseFields(actionField).find(field => field.number === 1)?.value;
    if (action !== 0n) return;
    const args = JSON.stringify({ seq: sequence });
    const flags = [[1, 6], [2, 2], [3, 1], [6, 2], [8, 3]];
    sendControl(encodeEnvelope(sequence, 3, simpleAction(1, args, flags)));
  }

  function sendSOAC(body) {
    if (!session) return Promise.resolve();
    return handlers.signal(session.id, body);
  }

  function candidateBody(candidate) {
    return {
      candidate: candidate.candidate,
      sdpMid: candidate.sdpMid ?? null,
      sdpMLineIndex: candidate.sdpMLineIndex ?? null,
    };
  }

  async function createPeer(bootstrap, options) {
    close();
    session = { id: options.id, bootstrap, purpose: options.purpose ?? "desktop", targetPlatform: bootstrap.targetPlatform ?? 1 };
    handlers = options;
    peer = new RTCPeerConnection({
      iceServers: bootstrap.iceServers ?? [],
      iceTransportPolicy: bootstrap.forceRelay ? "relay" : "all",
    });
    peer.onicecandidate = event => {
      if (!event.candidate) return;
      void sendSOAC({ type: "candidate", candidate: candidateBody(event.candidate) });
    };
    peer.oniceconnectionstatechange = () => {
      handlers.onState?.(peer.iceConnectionState === "connected" || peer.iceConnectionState === "completed" ? "connected" : peer.iceConnectionState);
    };
    peer.onconnectionstatechange = () => {
      if (peer.connectionState === "failed") handlers.onError?.("UU Remote WebRTC connection failed.");
    };
    peer.ontrack = event => {
      if (event.track.kind !== "video") return;
      const video = document.querySelector("[data-uu-video]");
      if (!video) return;
      video.srcObject = event.streams?.[0] ?? new MediaStream([event.track]);
      video.play().catch(() => {});
      handlers.onState?.("receiving video");
    };
    peer.ondatachannel = event => installChannel(event.channel);

    for (const label of ["STREAMER_DATA_CHANNEL", "FILE_DATA_CHANNEL", "TEXT_DATA_CHANNEL", "BINARY_DATA_CHANNEL", "CONTROL_DATA_CHANNEL"]) {
      const channel = peer.createDataChannel(label, { ordered: true });
      channel.binaryType = "arraybuffer";
      installChannel(channel);
    }
    for (let i = 0; i < 5; i++) peer.addTransceiver("video", { direction: "recvonly" });
    peer.addTransceiver("audio", { direction: "recvonly" });

    const offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    let sdp = offer.sdp ?? "";
    if (!sdp.includes("a=x-uuremote-quic")) sdp = sdp.replace(/a=group:BUNDLE[^\r\n]*/m, line => `a=x-uuremote-quic\r\n${line}`);
    if (!sdp.includes("a=max-message-size:")) sdp = sdp.replace(/(a=sctp-port:[^\r\n]*\r?\n)/g, "$1a=max-message-size:262144\r\n");
    await sendSOAC({ type: "offer", sdp, ice_network_type: 3 });
    handlers.onState?.("negotiating");
  }

  function installChannel(channel) {
    channels.set(channel.label, channel);
    channel.binaryType = "arraybuffer";
    channel.onopen = () => {
      if (channel.label === "CONTROL_DATA_CHANNEL") {
        sendHeartbeat();
        controlHeartbeat = setInterval(sendHeartbeat, 100);
        setTimeout(() => sendVideoRequest(), 1000);
        let ticks = 0;
        const keyboardHeartbeat = setInterval(() => {
          if (channel.readyState !== "open") { clearInterval(keyboardHeartbeat); return; }
          if (++ticks % 10 === 0) channel.send('{"heartbeat":"kbd_heartbeat"}');
        }, 100);
      }
      if (channel.label === "BINARY_DATA_CHANNEL" && session?.purpose === "terminal") openTerminal();
      handlers.onChannel?.(channel.label, "open");
    };
    channel.onclose = () => {
      if (channel.label === "CONTROL_DATA_CHANNEL" && controlHeartbeat !== null) {
        clearInterval(controlHeartbeat);
        controlHeartbeat = null;
      }
      handlers.onChannel?.(channel.label, "closed");
    };
    channel.onerror = () => handlers.onError?.(`${channel.label} failed.`);
    channel.onmessage = event => {
      if (channel.label === "CONTROL_DATA_CHANNEL" && event.data instanceof ArrayBuffer) {
        try { replyToEcho(event.data); } catch (error) { handlers.onError?.(String(error)); }
      } else if (channel.label === "BINARY_DATA_CHANNEL" && event.data instanceof ArrayBuffer) {
        handleTerminalFrame(new Uint8Array(event.data));
      }
    };
  }

  async function decodeSdp(body) {
    if (typeof body.sdp === "string") return body.sdp;
    if (typeof body.gzip_sdp !== "string" || typeof DecompressionStream === "undefined") return null;
    const binary = atob(body.gzip_sdp);
    const bytes = Uint8Array.from(binary, char => char.charCodeAt(0));
    const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"));
    return await new Response(stream).text();
  }

  async function handleSOAC(raw) {
    const payload = raw?.data && typeof raw.data === "object" ? raw.data : raw;
    if (raw?.client_id && raw.client_id !== session.bootstrap.clientID) return;
    if (payload.ice_id && payload.ice_id !== session.bootstrap.iceID) return;
    if (payload.app_control_id && payload.app_control_id !== session.bootstrap.appControlID) return;

    if (payload.type === "answer" || payload.type === "offer") {
      const sdp = await decodeSdp(payload);
      if (!sdp) return;
      if (payload.type === "offer") {
        await peer.setRemoteDescription({ type: "offer", sdp });
        const answer = await peer.createAnswer();
        await peer.setLocalDescription(answer);
        await sendSOAC({ type: "answer", sdp: answer.sdp });
      } else {
        await peer.setRemoteDescription({ type: "answer", sdp });
      }
      while (pendingCandidates.length) await peer.addIceCandidate(pendingCandidates.shift());
    } else if (payload.type === "candidate") {
      let candidate = payload.candidate;
      if (typeof candidate === "string") candidate = { candidate, sdpMid: payload.sdp_mid ?? null, sdpMLineIndex: payload.sdp_m_line_index ?? null };
      if (!candidate || typeof candidate !== "object" || !candidate.candidate) return;
      if (!peer.remoteDescription) pendingCandidates.push(candidate);
      else await peer.addIceCandidate(candidate);
    } else if (payload.type === "restart_ice") {
      const offer = await peer.createOffer({ iceRestart: true });
      await peer.setLocalDescription(offer);
      await sendSOAC({ type: "offer", sdp: offer.sdp, ice_network_type: 3 });
    }
  }

  async function handleSignals(signals) {
    if (!session || !peer) return;
    for (const signal of signals ?? []) {
      if (signal.name === "soac" || signal.name === "control") {
        try { await handleSOAC(signal.data); }
        catch (error) { handlers.onError?.(String(error)); }
      } else if (signal.name === "be-controlled") {
        handlers.onState?.("control accepted");
      }
    }
  }

  function sendInput(message) {
    const channel = channels.get("CONTROL_DATA_CHANNEL");
    if (channel?.readyState === "open") channel.send(JSON.stringify(message));
  }

  function sendTerminalFrame(kind, payload = new Uint8Array()) {
    const channel = channels.get("BINARY_DATA_CHANNEL");
    if (channel?.readyState !== "open" || payload.length > 0x100000) return;
    const frame = new Uint8Array(16 + payload.length);
    frame.set([84, 69, 82, 77, 1, kind, 0, 0]);
    const view = new DataView(frame.buffer);
    view.setUint32(8, terminalSessionID, true);
    view.setUint32(12, payload.length, true);
    frame.set(payload, 16);
    channel.send(frame);
  }

  function openTerminal(columns = 100, rows = 30) {
    const shell = session?.targetPlatform === 1 ? "powershell.exe" : "/bin/zsh";
    sendTerminalFrame(1, encoder.encode(JSON.stringify({ cols: columns, rows, shell, option: "none" })));
  }

  function sendTerminalInput(text) { sendTerminalFrame(2, encoder.encode(text)); }
  function resizeTerminal(columns, rows) { sendTerminalFrame(3, encoder.encode(JSON.stringify({ cols: columns, rows }))); }

  function handleTerminalFrame(frame) {
    if (frame.length < 16 || decoder.decode(frame.subarray(0, 4)) !== "TERM" || frame[4] !== 1) return;
    const view = new DataView(frame.buffer, frame.byteOffset, frame.byteLength);
    const sessionID = view.getUint32(8, true);
    const length = view.getUint32(12, true);
    if (length > 0x100000 || frame.length !== 16 + length || sessionID !== terminalSessionID) return;
    const payload = frame.subarray(16);
    switch (frame[5]) {
      case 5: handlers.onTerminalOutput?.(decoder.decode(payload)); break;
      case 6: {
        let code = null;
        try { code = JSON.parse(decoder.decode(payload)).exit_code; } catch {}
        handlers.onTerminalExit?.(code);
        break;
      }
      case 8:
      case 13: {
        let response = {};
        try { response = JSON.parse(decoder.decode(payload)); } catch {}
        if (frame[5] === 8 || response.code !== 0) handlers.onError?.(response.message || "UU Remote terminal could not be opened.");
        break;
      }
    }
  }

  function sendKey(key, down) {
    sendInput({ action: down ? "kbd_press" : "kbd_release", key });
  }

  function close() {
    clearInterval(controlHeartbeat);
    controlHeartbeat = null;
    for (const channel of channels.values()) {
      try { channel.close(); } catch {}
    }
    channels.clear();
    pendingCandidates.length = 0;
    if (peer) { try { peer.close(); } catch {} }
    peer = null;
    session = null;
  }

  window.UUViewer = {
    connect: createPeer,
    close,
    handleSignals,
    sendInput,
    sendKey,
    sendTerminalInput,
    resizeTerminal,
  };
})();
