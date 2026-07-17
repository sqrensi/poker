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
  queueing: false,
  minPlayers: 2,
  maxPlayers: 10,
  nickDirty: false,
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

function showInvite(code) {
  const link = `${location.origin}/?room=${code}`;
  $("inviteLink").value = link;
  $("roomCodeShow").textContent = code;
  history.replaceState({}, "", `/?room=${code}`);
}

function updateChip() {
  $("chipNick").textContent = state.nickname || "Игрок";
  $("chipRating").textContent = String(state.rating ?? 1000);
  $("chipIdShort").textContent = (state.playerId || "").slice(-8);
}

function setView(view) {
  $("home").classList.toggle("hidden", view !== "home");
  $("queueing").classList.toggle("hidden", view !== "queueing");
  $("waiting").classList.toggle("hidden", view !== "waiting");
  $("table").classList.toggle("hidden", view !== "table");
  document.body.classList.toggle("in-table", view === "table");
  if (view === "table") {
    // На телефоне прокрутить к доске / своим картам
    requestAnimationFrame(() => {
      const hole = $("hole");
      if (hole && window.matchMedia("(max-width: 720px)").matches) {
        hole.scrollIntoView({ block: "nearest", behavior: "smooth" });
      }
    });
  }
}

function connect() {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl());
    state.ws = ws;
    ws.onopen = () => resolve();
    ws.onerror = () => reject(new Error("Не удалось подключиться. Запущен ли npm start?"));
    ws.onmessage = (ev) => onMessage(JSON.parse(ev.data));
    ws.onclose = () => {
      const err = $("lobbyErr");
      if (err) err.textContent = "Соединение закрыто. Обновите страницу.";
    };
  });
}

async function refreshOnline() {
  try {
    const r = await fetch("/api/online");
    const j = await r.json();
    $("onlineCount").textContent = String(j.online ?? 0);
    $("queueCount").textContent = String(j.queue?.queueSize ?? 0);
  } catch {
    /* ignore */
  }
}

async function refreshLeaderboard() {
  try {
    const r = await fetch("/api/leaderboard");
    const j = await r.json();
    const ol = $("leaderboard");
    ol.innerHTML = "";
    (j.entries || []).slice(0, 12).forEach((e, i) => {
      const li = document.createElement("li");
      li.innerHTML = `<span class="rank">${i + 1}</span><span class="nm">${escapeHtml(
        e.nickname
      )}</span><span class="rt">${e.rating}</span>`;
      ol.appendChild(li);
    });
    if (!(j.entries || []).length) {
      ol.innerHTML = `<li><span class="nm">Пока пусто — сыграйте матч</span></li>`;
    }
  } catch {
    /* ignore */
  }
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function onMessage(msg) {
  if (msg.type === "welcome") {
    state.minPlayers = msg.minPlayers ?? 2;
    state.maxPlayers = msg.maxPlayers ?? 10;
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
    refreshLeaderboard();
    refreshOnline();

    // Auto-join room from URL after auth
    const params = new URLSearchParams(location.search);
    const roomFromUrl = (params.get("room") || "").toUpperCase();
    if (roomFromUrl && !state.room) {
      send({ type: "join", room: roomFromUrl, name: msg.nickname });
    }
    return;
  }

  if (msg.type === "error") {
    $("lobbyErr").textContent = msg.error || "Ошибка";
    $("nickHint").textContent = msg.error || "";
    return;
  }

  if (msg.type === "queue_status") {
    state.queueing = true;
    setView("queueing");
    $("btnQueue").classList.add("hidden");
    $("btnDequeue").classList.remove("hidden");
    $("queueStatus").textContent = `Место в очереди: ${msg.position} · в поиске: ${msg.queueSize} · ждём ${msg.waitedSec}с (стол ${msg.minPlayers}–${msg.maxPlayers})`;
    $("queueCount").textContent = String(msg.queueSize ?? 0);
    return;
  }

  if (msg.type === "queue_left") {
    state.queueing = false;
    setView("home");
    $("btnQueue").classList.remove("hidden");
    $("btnDequeue").classList.add("hidden");
    $("queueHint").textContent = "Поиск отменён.";
    refreshOnline();
    return;
  }

  if (msg.type === "matched") {
    state.queueing = false;
    $("btnQueue").classList.remove("hidden");
    $("btnDequeue").classList.add("hidden");
    $("lobbyErr").textContent = `Стол найден · ${msg.players} игроков`;
    return;
  }

  if (msg.type === "created" || msg.type === "joined") {
    state.room = msg.code;
    state.isHost = msg.type === "created";
    state.queueing = false;
    showInvite(msg.code);
    setView("waiting");
    if (state.isHost) {
      $("waitingTitle").textContent = "Вы хост — ждите игроков";
      $("waitingHint").textContent = "Скопируйте ссылку друзьям. Когда зайдут — «Начать матч».";
    } else {
      $("waitingTitle").textContent = "Вы в комнате";
      $("waitingHint").textContent = "Дождитесь, пока хост начнёт матч.";
    }
    return;
  }

  if (msg.type === "leaderboard") {
    const ol = $("leaderboard");
    ol.innerHTML = "";
    (msg.entries || []).forEach((e, i) => {
      const li = document.createElement("li");
      li.innerHTML = `<span class="rank">${i + 1}</span><span class="nm">${escapeHtml(
        e.nickname
      )}</span><span class="rt">${e.rating}</span>`;
      ol.appendChild(li);
    });
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

  if (!msg.started || !msg.table) {
    setView("waiting");
    showInvite(msg.code);

    const list = $("lobbyList");
    list.innerHTML = "";
    msg.lobby.forEach((p) => {
      const row = document.createElement("div");
      const tags = [];
      if (p.id === msg.hostId) tags.push("хост");
      if (p.id === state.playerId) tags.push("вы");
      if (!p.connected) tags.push("оффлайн");
      row.innerHTML = `<span><strong>${escapeHtml(p.name)}</strong>${
        tags.length ? " · " + tags.join(" · ") : ""
      }</span><span class="rating">${p.rating ?? "—"}</span>`;
      list.appendChild(row);
    });
    $("playerCount").textContent = String(msg.lobby.length);

    if (msg.fromQueue) {
      $("btnStart").classList.add("hidden");
      $("startHint").textContent = "Стол из очереди — старт автоматический…";
      $("waitingTitle").textContent = "Стол собран";
    } else {
      const canStart = state.isHost && msg.lobby.length >= 2;
      $("btnStart").classList.toggle("hidden", !canStart);
      if (state.isHost) {
        $("startHint").textContent = canStart
          ? "Все на месте — можно начинать."
          : "Нужен хотя бы ещё один игрок по ссылке.";
      } else {
        $("startHint").textContent = "Ждём хоста…";
        $("btnStart").classList.add("hidden");
      }
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
      // Крупные зоны нажатия на тач
      b.style.touchAction = "manipulation";
      actions.appendChild(b);
    };
    if (legal.canFold) add("Фолд", () => send({ type: "action", action: "fold" }), "danger");
    if (legal.canCheck) add("Чек", () => send({ type: "action", action: "check" }));
    if (legal.canCall) add(`Колл ${legal.callAmount}`, () => send({ type: "action", action: "call" }));
    if (legal.canBet)
      add(`Бет ${legal.minRaiseTo}`, () => send({ type: "action", action: "bet", amount: legal.minRaiseTo }), "primary");
    if (legal.canRaise)
      add(
        `Рейз ${legal.minRaiseTo}`,
        () => send({ type: "action", action: "raise", amount: legal.minRaiseTo }),
        "primary"
      );
    add("Олл-ин", () => send({ type: "action", action: "allin" }), "danger");
  }

  const matchOver = t.street === "matchComplete";
  $("btnNext").classList.toggle("hidden", !(state.isHost && t.street === "handComplete" && !msg.fromQueue));
  // Host of queue table can still advance hands
  if (msg.fromQueue && state.isHost && t.street === "handComplete") {
    $("btnNext").classList.remove("hidden");
  }
  $("btnRematch").classList.toggle("hidden", !(state.isHost && matchOver));

  const banner = $("matchBanner");
  if (matchOver) {
    banner.classList.remove("hidden");
    const w = t.players.find((p) => p.seat === t.matchWinner);
    banner.textContent = w
      ? w.id === state.playerId
        ? `Матч окончен — вы победили! Рейтинг обновлён.`
        : `Матч окончен — победитель: ${w.name}`
      : t.lastLog || "Матч окончен";
    refreshLeaderboard();
  } else {
    banner.classList.add("hidden");
  }
}

function updateOrientationGate() {
  const gate = $("rotateGate");
  if (!gate) return;
  // height > width надёжнее matchMedia на части Android-браузеров
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
    if (screen.orientation && screen.orientation.lock) {
      await screen.orientation.lock("landscape");
    }
  } catch {
    /* браузер может запретить без fullscreen — CSS-заглушка останется */
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

  // Меню видно сразу, даже если WS ещё не поднялся
  setView("home");

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

  $("btnQueue").onclick = () => {
    $("lobbyErr").textContent = "";
    send({ type: "queue", nickname: $("name").value });
  };
  const cancelQ = () => send({ type: "dequeue" });
  $("btnDequeue").onclick = cancelQ;
  $("btnDequeue2").onclick = cancelQ;

  $("btnCreate").onclick = () => {
    $("lobbyErr").textContent = "";
    send({ type: "create", name: $("name").value || "Хост" });
  };
  $("btnJoin").onclick = () => {
    $("lobbyErr").textContent = "";
    const code = ($("roomCode").value || "").trim().toUpperCase();
    if (!code) {
      $("lobbyErr").textContent = "Введите код комнаты";
      return;
    }
    send({ type: "join", room: code, name: $("name").value || "Игрок" });
  };
  $("btnStart").onclick = () => send({ type: "start" });
  $("btnNext").onclick = () => send({ type: "next" });
  $("btnRematch").onclick = () => send({ type: "rematch" });
  $("btnCopy").onclick = async () => {
    const link = $("inviteLink").value;
    try {
      await navigator.clipboard.writeText(link);
      $("btnCopy").textContent = "Скопировано!";
      setTimeout(() => ($("btnCopy").textContent = "Копировать"), 1500);
    } catch {
      $("inviteLink").select();
      $("lobbyErr").textContent = "Скопируйте ссылку вручную (Ctrl+C)";
    }
  };

  setInterval(refreshOnline, 4000);
  refreshOnline();
  refreshLeaderboard();
}

boot().catch((e) => {
  $("lobbyErr").textContent = e.message || String(e);
});
