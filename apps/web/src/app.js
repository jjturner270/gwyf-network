export const DEFAULT_API_BASE_URL = "http://127.0.0.1:3000";
export const DEFAULT_POLL_MS = 5000;
export const OBSERVER_TOOLTIP =
  "Distinct community observers that have seen this lobby recently.";

const STATUS_LABELS = {
  active: "LIVE",
  likely_active: "LIKELY ACTIVE",
  stale: "STALE",
};

export function statusLabel(status) {
  return Object.hasOwn(STATUS_LABELS, status) ? STATUS_LABELS[status] : null;
}

export function stripTrailingSlashes(url) {
  return url.replace(/\/+$/, "");
}

export function resolveApiBase(override) {
  if (typeof override === "string" && override.trim()) {
    return stripTrailingSlashes(override.trim());
  }
  if (
    typeof window !== "undefined" &&
    typeof window.GWYF_API_BASE_URL === "string" &&
    window.GWYF_API_BASE_URL.trim()
  ) {
    return stripTrailingSlashes(window.GWYF_API_BASE_URL.trim());
  }
  return DEFAULT_API_BASE_URL;
}

const ONE_MIN_MS = 60_000;
const ONE_HOUR_MS = 3_600_000;

export function formatAgo(ms) {
  if (!Number.isFinite(ms) || ms < 0) return "";
  if (ms < 1000) return "just now";
  if (ms < ONE_MIN_MS) return `${Math.floor(ms / 1000)}s ago`;
  if (ms < ONE_HOUR_MS) return `${Math.floor(ms / ONE_MIN_MS)}m ago`;
  return `${Math.floor(ms / ONE_HOUR_MS)}h ago`;
}

export function ageMsFrom(iso, nowMs) {
  if (!iso) return null;
  const t = new Date(iso).getTime();
  return Number.isNaN(t) ? null : Math.max(0, nowMs - t);
}

export function toCard(lobby, nowMs) {
  const age = ageMsFrom(lobby.lastSeenAt, nowMs);
  return {
    id: lobby.lobbyId ?? "",
    name: lobby.name || "Unnamed lobby",
    map: lobby.map?.name || "Unknown map",
    mode: lobby.mode || "Unknown mode",
    players: Number.isFinite(lobby.players) ? lobby.players : 0,
    capacity: Number.isFinite(lobby.capacity) ? lobby.capacity : 0,
    status: statusLabel(lobby.status),
    ago: age === null ? null : formatAgo(age),
    observerCount: Number.isFinite(lobby.observerCount) ? lobby.observerCount : 0,
  };
}

/**
 * One poll loop for GET /v1/lobbies/live. Injected fetch/clock make it fully
 * testable without a browser. Never throws: outcomes arrive via onUpdate
 * (online, with lobbies or empty) or onError (unreachable — never rendered as
 * "0 lobbies").
 */
export class LiveNetwork {
  constructor({
    apiBase,
    pollMs = DEFAULT_POLL_MS,
    fetchImpl = globalThis.fetch,
    onUpdate,
    onError,
    clock = () => Date.now(),
  }) {
    this.apiBase = resolveApiBase(apiBase);
    this.pollMs = pollMs;
    this.fetchImpl = fetchImpl;
    this.onUpdate = onUpdate;
    this.onError = onError;
    this.clock = clock;
    this._timer = null;
    this._inFlight = false;
    this._stopped = false;
    this._abort = new AbortController();
  }

  start() {
    this.refresh();
    this._timer = setInterval(() => this.refresh(), this.pollMs);
    if (this._timer.unref) this._timer.unref();
  }

  stop() {
    this._stopped = true;
    this._abort.abort();
    if (this._timer) clearInterval(this._timer);
    this._timer = null;
  }

  async refresh() {
    if (this._inFlight || this._stopped) return;
    this._inFlight = true;
    try {
      const response = await this.fetchImpl(`${this.apiBase}/v1/lobbies/live`, {
        signal: this._abort.signal,
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const payload = await response.json();
      const now = this.clock();
      const state = {
        online: true,
        lobbies: (Array.isArray(payload.lobbies) ? payload.lobbies : []).map((l) => toCard(l, now)),
        activeObservers: Number.isFinite(payload.activeObservers) ? payload.activeObservers : null,
        ageMs: ageMsFrom(payload.generatedAt, now),
      };
      if (!this._stopped) this.onUpdate(state);
    } catch (err) {
      if (!this._stopped) this.onError(err instanceof Error ? err : new Error(String(err)));
    } finally {
      this._inFlight = false;
    }
  }
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function renderStatus(status, state) {
  status.className = "status status-online";
  const parts = ["NETWORK ONLINE"];
  if (state.activeObservers !== null) {
    parts.push(`${state.activeObservers} ${state.activeObservers === 1 ? "ACTIVE OBSERVER" : "ACTIVE OBSERVERS"}`);
  }
  if (state.ageMs !== null) {
    parts.push(`UPDATED ${formatAgo(state.ageMs).toUpperCase()}`);
  }
  status.textContent = "● " + parts.join(" · ");
}

function renderOffline(status, cards) {
  status.className = "status status-offline";
  status.textContent = "● COMMUNITY NETWORK UNAVAILABLE · TRYING AGAIN…";
  cards.textContent = "";
  const state = el("p", "message");
  state.textContent = "Community network unavailable. Trying again…";
  cards.append(state);
}

function renderCards(cards, state) {
  cards.textContent = "";
  if (state.lobbies.length === 0) {
    const empty = el("p", "message");
    empty.textContent = "No lobbies are currently being observed.\nThe network only knows what community observers can see.";
    cards.append(empty);
    return;
  }
  for (const lobby of state.lobbies) {
    const card = el("article", "card");
    const head = el("div", "card-head");
    head.append(el("span", "name", lobby.name));
    if (lobby.status) head.append(el("span", `badge badge-${lobby.status.toLowerCase().replaceAll(" ", "-")}`, lobby.status));
    card.append(head);
    card.append(el("div", "meta", `${lobby.map} · ${lobby.mode}`));
    card.append(el("div", "players", `${lobby.players} / ${lobby.capacity} players`));
    const seen = el("div", "seen", `Seen by ${lobby.observerCount} ${lobby.observerCount === 1 ? "observer" : "observers"}${lobby.ago ? ` · ${lobby.ago}` : ""}`);
    seen.title = OBSERVER_TOOLTIP;
    card.append(seen);
    cards.append(card);
  }
}

export function mount(root, { apiBase, pollMs } = {}) {
  root.textContent = "";
  const h1 = document.createElement("h1");
  h1.textContent = "GWYF COMMUNITY";
  const status = el("p", "status");
  const cards = el("section", "cards");
  const footnote = el("p", "footnote");
  footnote.textContent =
    "Observer counts are distinct community observers that have seen a lobby recently — not players. The network only knows what community observers can see.";
  root.append(h1, status, cards, footnote);

  const network = new LiveNetwork({
    apiBase,
    pollMs,
    fetchImpl: globalThis.fetch,
    onUpdate: (state) => {
      renderStatus(status, state);
      renderCards(cards, state);
    },
    onError: () => renderOffline(status, cards),
  });
  network.start();
  return network;
}

if (typeof document !== "undefined" && typeof window !== "undefined") {
  const root = document.getElementById("app");
  if (root) {
    const apiOverride = new URLSearchParams(window.location.search).get("api") ?? undefined;
    mount(root, { apiBase: apiOverride });
  }
}