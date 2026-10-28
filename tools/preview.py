"""UI preview without the app: builds .build/preview/index.html = wwwroot/index.html + a fake host.

    python tools/preview.py

Open the result in any browser. Query parameters switch states:
    ?theme=dark | ?theme=light      theme (default: system)
    ?state=empty | ?state=error     empty period / failing host
    ?paused=1                       tracking paused
    ?update=available | ready       a new version is found / downloaded
    ?mode=week | month | all        open another period (for screenshots)
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "TimeTracker" / "wwwroot" / "index.html"
OUT = ROOT / ".build" / "preview" / "index.html"

MOCK = r"""
<script>
(function () {
  const q = new URLSearchParams(location.search);
  window.ttTheme = q.get("theme") || "system";
  const mode = q.get("state");
  let paused = q.get("paused") === "1";
  let exclude = ["explorer.exe"];
  const APPS = [
    ["blender.exe", "Blender", ["robot_rig_v3.blend — Blender", "scene_city.blend — Blender", "Preferences"]],
    ["fl64.exe", "FL Studio", ["night_drive.flp — FL Studio 21", "beat_sketch_04.flp — FL Studio 21"]],
    ["chrome.exe", "Google Chrome", ["YouTube — Google Chrome", "Blender Manual — Google Chrome", "Gmail — Google Chrome", "GitHub — Google Chrome", "Stack Overflow — Google Chrome", "Docs — Google Chrome", "Reddit — Google Chrome", "Wikipedia — Google Chrome", "Translate — Google Chrome", "Maps — Google Chrome"]],
    ["code.exe", "VS Code", ["tracker.py — TimeTracker — VS Code", "index.html — TimeTracker — VS Code"]],
    ["unity.exe", "Unity", ["Platformer — Main.unity — Unity 6"]],
    ["telegram.exe", "Telegram", ["Saved Messages", "Робочий чат"]],
    ["ms-teams.exe", "Microsoft Teams", ["Лекція — Microsoft Teams"]],
    ["steamwebhelper.exe", "Steam", ["Steam"]],
    ["discord.exe", "Discord", ["#general — Discord"]],
    ["devenv.exe", "Visual Studio", ["TimeTracker — Visual Studio"]],
    ["taskmgr.exe", "Диспетчер завдань", ["Диспетчер завдань"]],
  ];
  const pad = (n) => String(n).padStart(2, "0");
  const parse = (s) => { const [y, m, d] = s.split("-").map(Number); return new Date(y, m - 1, d); };
  const iso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  function rnd(seed) { return () => (seed = (seed * 16807) % 2147483647) / 2147483647; }

  // Deterministic fake day: sessions between ~9:00 and ~22:00, weighted towards the first apps.
  function daySpans(date) {
    const r = rnd(date.getFullYear() * 400 + date.getMonth() * 31 + date.getDate());
    if (r() < 0.12) return [];
    const day0 = date.getTime() / 1000, out = [];
    let t = (8.5 + r() * 2) * 3600;
    const now = Date.now() / 1000 - day0;
    const end = Math.min((19 + r() * 3.5) * 3600, now);
    while (t < end) {
      const i = Math.min(APPS.length - 1, Math.floor(Math.pow(r(), 1.7) * APPS.length));
      const len = (3 + r() * (i < 3 ? 70 : 20)) * 60;
      const e = Math.min(end, t + len);
      if (!exclude.includes(APPS[i][0])) out.push({ s: t, e, i, title: APPS[i][2][Math.floor(r() * APPS[i][2].length)] });
      t = e + (r() < 0.2 ? r() * 40 * 60 : r() * 90);
    }
    return out;
  }
  function overview(from, to) {
    const perApp = {}, hours = Array.from({ length: 24 }, () => ({})), days = [];
    let timeline = from === to ? [] : null, first = null, last = null;
    for (let d = parse(from); d <= parse(to); d.setDate(d.getDate() + 1)) {
      const apps = {}, day0 = d.getTime() / 1000;
      for (const sp of daySpans(new Date(d))) {
        const name = APPS[sp.i][1], dur = sp.e - sp.s;
        perApp[name] = (perApp[name] || 0) + dur;
        apps[name] = (apps[name] || 0) + dur;
        for (let s = sp.s; s < sp.e;) {
          const h = Math.floor(s / 3600), ce = Math.min(sp.e, (h + 1) * 3600);
          hours[h][name] = (hours[h][name] || 0) + ce - s;
          s = ce;
        }
        if (timeline) {
          const lastSeg = timeline[timeline.length - 1];
          if (lastSeg && lastSeg.app === name && sp.s - lastSeg.e < 15) lastSeg.e = sp.e;
          else timeline.push({ s: sp.s, e: sp.e, app: name });
        }
        first = first === null ? day0 + sp.s : Math.min(first, day0 + sp.s);
        last = last === null ? day0 + sp.e : Math.max(last, day0 + sp.e);
      }
      days.push({ date: iso(d), apps });
    }
    const apps = Object.entries(perApp).sort((a, b) => b[1] - a[1])
      .map(([name, seconds]) => ({ name, exe: APPS.find((a) => a[1] === name)[0], seconds }));
    return { total: apps.reduce((s, a) => s + a.seconds, 0), apps, days, hours, timeline, firstUnix: first, lastUnix: last };
  }
  const firstDay = (() => { const d = new Date(); d.setDate(d.getDate() - 150); return iso(d); })();
  const upd = q.get("update");
  let update = { source: "release", current: "2.1.0", latest: upd ? "2.2.0" : "2.1.0",
                 available: upd === "available", ready: upd === "ready", error: null, checked: !!upd };
  const settings = { idleMinutes: 2, autostart: true, theme: window.ttTheme, autoUpdate: true, version: "2.1.0" };
  const settingsView = () => ({ ...settings, exclude: exclude.map((exe) => ({ exe, name: (APPS.find((a) => a[0] === exe) || [exe, exe === "explorer.exe" ? "Провідник" : exe])[1] })) });

  const handlers = {
    overview: (a) => mode === "empty" ? { total: 0, apps: [], days: overview(a.from, a.to).days.map((d) => ({ date: d.date, apps: {} })), hours: Array.from({ length: 24 }, () => ({})), timeline: [], firstUnix: null, lastUnix: null } : overview(a.from, a.to),
    total: (a) => mode === "empty" ? 0 : overview(a.from, a.to).total,
    firstDay: () => mode === "empty" ? null : firstDay,
    updateState: () => update,
    checkUpdates: () => update = { ...update, checked: true },
    installUpdate: () => update = { ...update, available: false, ready: true },
    restartToUpdate: () => null,
    openRepo: () => null,
    status: () => ({ paused, current: paused ? null : "Blender", todayTotal: 0 }),
    setPaused: (a) => { paused = a.value; return null; },
    settings: () => settingsView(),
    programs: () => APPS.map(([exe, name]) => ({ exe, name, seconds: 1 })),
    updateSettings: (a) => { Object.assign(settings, a); return settingsView(); },
    exclude: (a) => { if (!exclude.includes(a.exe)) exclude.push(a.exe); return settingsView(); },
    include: (a) => { exclude = exclude.filter((e) => e !== a.exe); return settingsView(); },
    export: () => "C:\\Users\\you\\Documents\\TimeTracker_preview.csv",
    openDataFolder: () => null, revealFile: () => null, systemTheme: () => null,
  };
  if (q.get("mode")) addEventListener("DOMContentLoaded", () =>
    setTimeout(() => document.querySelector(`[data-mode="${q.get("mode")}"]`).click(), 300));
  const listeners = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener: (type, fn) => listeners.push(fn),
    postMessage: ({ id, method, args }) => setTimeout(() => {
      let msg;
      if (mode === "error" && method === "overview") msg = { type: "reply", id, ok: false, error: "database is locked" };
      else try { msg = { type: "reply", id, ok: true, data: handlers[method](args) }; }
      catch (e) { msg = { type: "reply", id, ok: false, error: String(e) }; }
      listeners.forEach((fn) => fn({ data: msg }));
    }, 120),
  };
})();
</script>
"""


def main():
    html = SRC.read_text(encoding="utf-8")
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(html.replace("<head>", "<head>" + MOCK, 1), encoding="utf-8")
    print(OUT)


if __name__ == "__main__":
    main()
