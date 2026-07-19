const $ = (id) => document.getElementById(id);

const LS_ID = "holdem_player_id_v1";
const LS_NICK = "holdem_nickname_v1";

const state = {
  ws: null,
  playerId: null,
  nickname: "",
  rating: 1000,
  room: null,
  isHost: false,
  last: null,
  view: "home",
  maxPlayers: 10,
  nickDirty: false,
  roomsTimer: null,
};

function wsUrl() {
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  return `${proto}//${location.host}`;
}

function getOrCreatePlayerId() {
  const params = new URLSearchParams(location.search);
  const fromUrl = params.get("pid");
  if (fromUrl && fromUrl.length >= 8) {
    localStorage.setItem(LS_ID, fromUrl);
    return fromUrl;
  }
  let id = localStorage.getItem(LS_ID);
  if (!id) {
    id =
      "player-" +
      (crypto.randomUUID?.() || `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 10)}`);
    localStorage.setItem(LS_ID, id);
  }
  return id;
}

function send(msg) {
  if (state.ws?.readyState === 1) state.ws.send(JSON.stringify(msg));
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function updateChip() {
  $("chipNick").textContent = state.nickname || "Игрок";
  $("chipRating").textContent = String(state.rating ?? 1000);
}

function setView(view) {
  state.view = view;
  const views = ["home", "onlineHub", "rooms", "waiting", "table"];
  for (const v of views) {
    const el = $(v);
    if (el) el.classList.toggle("hidden", v !== view);
  }
  document.body.classList.toggle("in-table", view === "table");

  if (view === "rooms") startRoomsPoll();
  else stopRoomsPoll();

  if (view === "table") {
    requestAnimationFrame(() => {
      const hole = $("hole");
      if (hole && window.matchMedia("(max-width: 720px)").matches) {
        hole.scrollIntoView({ block: "nearest", behavior: "smooth" });
      }
    });
  }
}

function startRoomsPoll() {
  stopRoomsPoll();
  refreshRooms();
  state.roomsTimer = setInterval(refreshRooms, 2500);
}

function stopRoomsPoll() {
  if (state.roomsTimer) {
    clearInterval(state.roomsTimer);
    state.roomsTimer = null;
  }
}

function refreshRooms() {
  send({ type: "list_rooms" });
}

function renderRoomList(rooms) {
  const list = $("roomList");
  list.innerHTML = "";
  if (!rooms?.length) {
    list.innerHTML = `<div class="room-empty">Пока нет открытых комнат.<br>Создайте свою или зайдите по коду.</div>`;
    return;
  }
  for (const r of rooms) {
    const row = document.createElement("button");
    row.type = "button";
    row.className = "room-card";
    row.innerHTML = `
      <div class="room-card-main">
        <span class="room-code">${escapeHtml(r.code)}</span>
        <span class="room-host">хост · ${escapeHtml(r.hostName)}</span>
      </div>
      <div class="room-card-meta">
        <span>${r.players}/${r.maxPlayers}</span>
        ${r.bots ? `<span class="bot-tag">${r.bots} бот</span>` : ""}
        <span class="join-label">Войти →</span>
      </div>`;
    row.onclick = () => {
      $("roomsErr").textContent = "";
      send({ type: "join", room: r.code, name: state.nickname || "Игрок" });
    };
    list.appendChild(row);
  }
}

function connect() {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl());
    state.ws = ws;
    ws.onopen = () => resolve();
    ws.onerror = () => reject(new Error("Не удалось подключиться к серверу"));
    ws.onmessage = (ev) => onMessage(JSON.parse(ev.data));
    ws.onclose = () => {
      const err = $("lobbyErr");
      if (err && state.view !== "table") err.textContent = "Соединение закрыто. Обновите страницу.";
    };
  });
}

function onMessage(msg) {
  if (msg.type === "welcome") {
    state.maxPlayers = msg.maxPlayers ?? 10;
    if ($("maxSeats")) $("maxSeats").textContent = String(state.maxPlayers);
    const nick = localStorage.getItem(LS_NICK) || $("name").value || "";
    send({ type: "auth", playerId: state.playerId, nickname: nick });
    return;
  }

  if (msg.type === "profile") {
    state.playerId = msg.playerId;
    state.nickname = msg.nickname;
    state.rating = msg.rating;
    localStorage.setItem(LS_ID, msg.playerId);
    localStorage.setItem(LS_NICK, msg.nickname);
    $("name").value = msg.nickname;
    $("name").disabled = true;
    $("btnEditNick").classList.remove("hidden");
    $("btnSaveNick").classList.add("hidden");
    if (state.nickDirty) {
      $("nickHint").textContent = "Ник сохранён";
      state.nickDirty = false;
    }
    updateChip();

    const params = new URLSearchParams(location.search);
    const roomFromUrl = (params.get("room") || "").toUpperCase();
    if (roomFromUrl && !state.room) {
      send({ type: "join", room: roomFromUrl, name: msg.nickname });
    }
    return;
  }

  if (msg.type === "error") {
    const target =
      state.view === "rooms" ? $("roomsErr") : $("lobbyErr") || $("roomsErr") || $("startHint");
    if (target) target.textContent = msg.error || "Ошибка";
    if ($("nickHint") && msg.error) $("nickHint").textContent = msg.error;
    return;
  }

  if (msg.type === "rooms") {
    renderRoomList(msg.rooms || []);
    return;
  }

  if (msg.type === "created" || msg.type === "joined") {
    state.room = msg.code;
    state.isHost = msg.type === "created";
    history.replaceState({}, "", `/?room=${msg.code}`);
    $("roomCodeShow").textContent = msg.code;
    setView("waiting");
    if (state.isHost) {
      $("waitingTitle").textContent = "Вы хост";
      $("waitingHint").textContent = "Добавьте ботов или дождитесь игроков — начать можно в любой момент.";
    } else {
      $("waitingTitle").textContent = "Вы в комнате";
      $("waitingHint").textContent = "Ждём, пока хост начнёт игру.";
    }
    return;
  }

  if (msg.type === "bots_added") {
    return;
  }

  if (msg.type === "state") {
    state.last = msg;
    state.room = msg.code;
    state.isHost = msg.hostId === state.playerId;
    render(msg);
  }
}

function suitClass(code) {
  return code.endsWith("H") || code.endsWith("D") ? "red" : "";
}

function prettyCard(code) {
  if (code === "??") return "?";
  const r = code[0] === "T" ? "10" : code[0];
  const s = { C: "♣", D: "♦", H: "♥", S: "♠" }[code[1]] || code[1];
  return r + s;
}

function cardEl(code) {
  const d = document.createElement("div");
  if (!code || code === "??") {
    d.className = "card-back";
    d.textContent = "🂠";
  } else {
    d.className = `card-face ${suitClass(code)}`;
    d.textContent = prettyCard(code);
  }
  return d;
}

function render(msg) {
  if ($("lobbyErr")) $("lobbyErr").textContent = "";
  if ($("roomsErr")) $("roomsErr").textContent = "";

  if (!msg.started || !msg.table) {
    setView("waiting");
    $("roomCodeShow").textContent = msg.code;

    const list = $("lobbyList");
    list.innerHTML = "";
    msg.lobby.forEach((p) => {
      const row = document.createElement("div");
      row.className = "lobby-row" + (p.isBot ? " is-bot" : "");
      const tags = [];
      if (p.id === msg.hostId) tags.push("хост");
      if (p.id === state.playerId) tags.push("вы");
      if (p.isBot) tags.push("бот");
      if (!p.connected) tags.push("оффлайн");
      row.innerHTML = `<span><strong>${escapeHtml(p.name)}</strong>${
        tags.length ? `<em>${tags.join(" · ")}</em>` : ""
      }</span><span class="rating">${p.isBot ? "—" : p.rating ?? "—"}</span>`;
      list.appendChild(row);
    });
    $("playerCount").textContent = String(msg.lobby.length);

    const humans = msg.lobby.filter((p) => !p.isBot).length;
    const bots = msg.lobby.filter((p) => p.isBot).length;
    const canStart = state.isHost && msg.lobby.length >= 2 && !msg.fromQueue;
    const canAddBot = state.isHost && !msg.fromQueue && msg.lobby.length < state.maxPlayers;

    $("btnStart").classList.toggle("hidden", !canStart);
    $("btnAddBot").classList.toggle("hidden", !canAddBot);

    if (msg.fromQueue) {
      $("startHint").textContent = "Стол собран — старт…";
    } else if (state.isHost) {
      if (msg.lobby.length < 2) {
        $("startHint").textContent = "Нужен ещё игрок или бот.";
      } else {
        $("startHint").textContent =
          bots || humans > 1
            ? "Можно начинать. Ботов можно добавить ещё."
            : "Можно начинать.";
      }
    } else {
      $("startHint").textContent = "Ждём хоста…";
    }
    return;
  }

  setView("table");

  const t = msg.table;
  $("meta").textContent = `Стол ${msg.code} · Раздача №${t.handNumber} · ${t.street} · Банк ${t.pot}`;
  $("log").textContent = t.lastLog || "";

  const board = $("board");
  board.innerHTML = "";
  t.board.forEach((c) => board.appendChild(cardEl(c)));

  const seats = $("seats");
  seats.innerHTML = "";
  t.players.forEach((p) => {
    const out = p.eliminated || p.chips <= 0;
    const d = document.createElement("div");
    d.className =
      "seat" +
      (p.seat === t.acting ? " acting" : "") +
      (p.folded ? " folded" : "") +
      (out ? " eliminated" : "");
    const tags = [];
    if (p.seat === t.dealer && !out) tags.push("D");
    if (p.seat === t.sbSeat && !out) tags.push("МБ");
    if (p.seat === t.bbSeat && !out) tags.push("ББ");
    d.innerHTML =
      `<strong>${escapeHtml(p.name)}</strong> <span class="rating">${p.rating ?? ""}</span> ${tags.join(" ")}` +
      `<br>${p.chips} фишек` +
      (p.betStreet && !out ? `<br>ставка ${p.betStreet}` : "") +
      (out ? "<br>ВЫБЫЛ" : "") +
      (!out && p.folded ? "<br>фолд" : "") +
      (!out && p.allIn ? "<br>олл-ин" : "");
    seats.appendChild(d);
  });

  const me = t.players.find((p) => p.id === state.playerId);
  if (me) {
    state.rating = me.rating ?? state.rating;
    updateChip();
  }

  const hole = $("hole");
  hole.innerHTML = "";
  if (!me?.eliminated) {
    (me?.hole || []).forEach((c) => hole.appendChild(cardEl(c)));
  }

  const actions = $("actions");
  actions.innerHTML = "";
  const legal = t.legal;
  if (legal && t.street !== "matchComplete") {
    const add = (label, fn, cls = "") => {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = label;
      if (cls) b.className = cls;
      b.onclick = (ev) => {
        ev.preventDefault();
        fn();
      };
      b.style.touchAction = "manipulation";
      actions.appendChild(b);
    };
    if (legal.canFold) add("Фолд", () => send({ type: "action", action: "fold" }), "danger");
    if (legal.canCheck) add("Чек", () => send({ type: "action", action: "check" }));
    if (legal.canCall) add(`Колл ${legal.callAmount}`, () => send({ type: "action", action: "call" }));

    if (legal.canBet || legal.canRaise) {
      const wrap = document.createElement("div");
      wrap.className = "raise-slider";
      const min = legal.minRaiseTo;
      const max = Math.max(min, legal.maxRaiseTo);
      const label = document.createElement("div");
      label.className = "raise-slider-label";
      const range = document.createElement("input");
      range.type = "range";
      range.min = String(min);
      range.max = String(max);
      range.step = "1";
      range.value = String(min);
      const sync = () => {
        const v = Number(range.value);
        const atMax = v >= max;
        label.textContent = atMax
          ? `Олл-ин · ${v}`
          : legal.canBet
            ? `Бет · ${v}`
            : `Рейз до · ${v}`;
      };
      sync();
      range.oninput = sync;
      wrap.appendChild(label);
      wrap.appendChild(range);
      actions.appendChild(wrap);

      add(legal.canBet ? "Бет" : "Рейз", () => {
        const amount = Number(range.value);
        send({
          type: "action",
          action: legal.canBet ? "bet" : "raise",
          amount,
        });
      }, "primary");
    }
    add("Олл-ин", () => send({ type: "action", action: "allin" }), "danger");
  }

  const matchOver = t.street === "matchComplete";
  $("btnNext").classList.toggle("hidden", !(state.isHost && t.street === "handComplete"));
  $("btnRematch").classList.toggle("hidden", !(state.isHost && matchOver));

  const banner = $("matchBanner");
  if (matchOver) {
    banner.classList.remove("hidden");
    const w = t.players.find((p) => p.seat === t.matchWinner);
    banner.textContent = w
      ? w.id === state.playerId
        ? `Матч окончен — вы победили!`
        : `Матч окончен — победитель: ${w.name}`
      : t.lastLog || "Матч окончен";
  } else {
    banner.classList.add("hidden");
  }
}

function updateOrientationGate() {
  const gate = $("rotateGate");
  if (!gate) return;
  const w = window.innerWidth;
  const h = window.innerHeight;
  const portrait = h > w;
  const phone = Math.min(w, h) < 900;
  const needRotate = portrait && phone;
  gate.hidden = !needRotate;
  document.body.classList.toggle("need-rotate", needRotate);
}

async function lockLandscape() {
  try {
    if (screen.orientation?.lock) await screen.orientation.lock("landscape");
  } catch {
    /* ignore */
  }
}

async function boot() {
  updateOrientationGate();
  window.addEventListener("orientationchange", updateOrientationGate);
  window.addEventListener("resize", updateOrientationGate);
  lockLandscape();

  state.playerId = getOrCreatePlayerId();
  const savedNick = localStorage.getItem(LS_NICK);
  if (savedNick) $("name").value = savedNick;
  const params = new URLSearchParams(location.search);
  const roomFromUrl = (params.get("room") || "").toUpperCase();
  if (roomFromUrl) $("roomCode").value = roomFromUrl;

  setView(roomFromUrl ? "home" : "home");

  try {
    await connect();
  } catch (e) {
    $("lobbyErr").textContent = e.message || String(e);
  }

  $("btnEditNick").onclick = () => {
    $("name").disabled = false;
    $("name").focus();
    $("btnEditNick").classList.add("hidden");
    $("btnSaveNick").classList.remove("hidden");
    $("nickHint").textContent = "Введите ник (3–16) и сохраните";
  };

  $("btnSaveNick").onclick = () => {
    $("lobbyErr").textContent = "";
    state.nickDirty = true;
    send({ type: "set_nickname", nickname: $("name").value });
  };

  $("btnGoOnline").onclick = () => {
    $("lobbyErr").textContent = "";
    setView("onlineHub");
  };

  $("btnBackHome").onclick = () => setView("home");
  $("btnBackHub").onclick = () => setView("onlineHub");

  $("btnCreate").onclick = () => {
    $("lobbyErr").textContent = "";
    send({ type: "create", name: $("name").value || "Хост", publicBase: location.origin });
  };

  $("btnConnect").onclick = () => {
    $("roomsErr").textContent = "";
    setView("rooms");
  };

  $("btnRefreshRooms").onclick = () => refreshRooms();

  $("btnJoin").onclick = () => {
    $("roomsErr").textContent = "";
    const code = ($("roomCode").value || "").trim().toUpperCase();
    if (!code) {
      $("roomsErr").textContent = "Введите код комнаты";
      return;
    }
    send({ type: "join", room: code, name: $("name").value || "Игрок" });
  };

  $("btnAddBot").onclick = () => send({ type: "add_bot", count: 1 });
  $("btnStart").onclick = () => send({ type: "start" });
  $("btnNext").onclick = () => send({ type: "next" });
  $("btnRematch").onclick = () => send({ type: "rematch" });
}

boot().catch((e) => {
  $("lobbyErr").textContent = e.message || String(e);
});
