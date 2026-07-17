const $ = (id) => document.getElementById(id);

const state = {
  ws: null,
  playerId: null,
  room: null,
  isHost: false,
  last: null,
};

function wsUrl() {
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  return `${proto}//${location.host}`;
}

function connect() {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl());
    state.ws = ws;
    ws.onopen = () => resolve();
    ws.onerror = () => reject(new Error("Не удалось подключиться к серверу. Запущен ли npm start?"));
    ws.onmessage = (ev) => onMessage(JSON.parse(ev.data));
    ws.onclose = () => {
      const err = $("lobbyErr");
      if (err) err.textContent = "Соединение закрыто. Обновите страницу.";
    };
  });
}

function send(msg) {
  state.ws.send(JSON.stringify(msg));
}

function showInvite(code) {
  const link = `${location.origin}/?room=${code}`;
  $("inviteLink").value = link;
  $("roomCodeShow").textContent = code;
  history.replaceState({}, "", `/?room=${code}`);
}

function onMessage(msg) {
  if (msg.type === "welcome") {
    state.playerId = msg.playerId;
    return;
  }
  if (msg.type === "error") {
    $("lobbyErr").textContent = msg.error || "Ошибка";
    return;
  }
  if (msg.type === "created" || msg.type === "joined") {
    state.room = msg.code;
    state.isHost = msg.type === "created";
    showInvite(msg.code);
    $("home").classList.add("hidden");
    $("waiting").classList.remove("hidden");
    if (state.isHost) {
      $("waitingTitle").textContent = "Вы хост — ждите игроков";
      $("waitingHint").textContent = "Скопируйте ссылку и отправьте друзьям. Когда зайдут — нажмите «Начать матч».";
    } else {
      $("waitingTitle").textContent = "Вы в комнате";
      $("waitingHint").textContent = "Дождитесь, пока хост начнёт матч.";
    }
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

  // Ещё не стартовали — лобби ожидания
  if (!msg.started || !msg.table) {
    $("home").classList.add("hidden");
    $("waiting").classList.remove("hidden");
    $("table").classList.add("hidden");
    showInvite(msg.code);

    const list = $("lobbyList");
    list.innerHTML = "";
    msg.lobby.forEach((p) => {
      const row = document.createElement("div");
      const tags = [];
      if (p.id === msg.hostId) tags.push("хост");
      if (p.id === state.playerId) tags.push("вы");
      if (!p.connected) tags.push("оффлайн");
      row.innerHTML = `<strong>${p.name}</strong>${tags.length ? " · " + tags.join(" · ") : ""}`;
      list.appendChild(row);
    });
    $("playerCount").textContent = String(msg.lobby.length);

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
    return;
  }

  // Матч идёт
  $("home").classList.add("hidden");
  $("waiting").classList.add("hidden");
  $("table").classList.remove("hidden");

  const t = msg.table;
  $("meta").textContent = `Комната ${msg.code} · Раздача №${t.handNumber} · ${t.street} · Банк ${t.pot}`;
  $("log").textContent = t.lastLog || "";

  const board = $("board");
  board.innerHTML = "";
  t.board.forEach((c) => board.appendChild(cardEl(c)));

  const seats = $("seats");
  seats.innerHTML = "";
  t.players.forEach((p) => {
    const d = document.createElement("div");
    d.className = "seat" + (p.seat === t.acting ? " acting" : "") + (p.folded ? " folded" : "");
    const tags = [];
    if (p.seat === t.dealer) tags.push("D");
    if (p.seat === t.sbSeat) tags.push("МБ");
    if (p.seat === t.bbSeat) tags.push("ББ");
    d.innerHTML =
      `<strong>${p.name}</strong> ${tags.join(" ")}<br>${p.chips} фишек` +
      (p.betStreet ? `<br>ставка ${p.betStreet}` : "") +
      (p.folded ? "<br>фолд" : "") +
      (p.allIn ? "<br>олл-ин" : "");
    seats.appendChild(d);
  });

  const me = t.players.find((p) => p.id === state.playerId);
  const hole = $("hole");
  hole.innerHTML = "";
  (me?.hole || []).forEach((c) => hole.appendChild(cardEl(c)));

  const actions = $("actions");
  actions.innerHTML = "";
  const legal = t.legal;
  if (legal) {
    const add = (label, fn, cls = "") => {
      const b = document.createElement("button");
      b.textContent = label;
      if (cls) b.className = cls;
      b.onclick = fn;
      actions.appendChild(b);
    };
    if (legal.canFold) add("Фолд", () => send({ type: "action", action: "fold" }), "danger");
    if (legal.canCheck) add("Чек", () => send({ type: "action", action: "check" }));
    if (legal.canCall) add(`Колл ${legal.callAmount}`, () => send({ type: "action", action: "call" }));
    if (legal.canBet) add(`Бет ${legal.minRaiseTo}`, () => send({ type: "action", action: "bet", amount: legal.minRaiseTo }), "primary");
    if (legal.canRaise) add(`Рейз ${legal.minRaiseTo}`, () => send({ type: "action", action: "raise", amount: legal.minRaiseTo }), "primary");
    add("Олл-ин", () => send({ type: "action", action: "allin" }), "danger");
  }

  $("btnNext").classList.toggle("hidden", !(state.isHost && t.street === "handComplete"));
}

async function boot() {
  const params = new URLSearchParams(location.search);
  const roomFromUrl = (params.get("room") || "").toUpperCase();
  if (roomFromUrl) $("roomCode").value = roomFromUrl;

  await connect();

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

  // Гость открыл ссылку хоста → сразу войти в комнату
  if (roomFromUrl) {
    setTimeout(() => {
      if (!state.room) {
        send({ type: "join", room: roomFromUrl, name: $("name").value || "Игрок" });
      }
    }, 200);
  }
}

boot().catch((e) => {
  $("lobbyErr").textContent = String(e.message || e);
});
