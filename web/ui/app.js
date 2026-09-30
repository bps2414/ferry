// Ferry web: fila ao vivo por SSE, configurações salvas campo a campo, upload em blocos com retomada.
"use strict";
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const localizedText = new Map();
function bindText(el, value) { localizedText.set(el, value); text(el, renderMessage(value)); }
function errorMessage(error) { return error.data?.errorMessage || error.messageData || error.message; }
function rerenderTranslations() {
  $("#authTitle").textContent = t(setupMode ? "ui.createAccess" : "ui.login");
  $("#authHint").textContent = setupMode ? t("ui.setupHint") : "";
  $("#authBtn").textContent = t(setupMode ? "ui.createSignIn" : "ui.login");
  for (const [el, value] of localizedText) text(el, renderMessage(value));
  if (lastState) render(lastState, false);
  for (const u of uploads.values()) renderUpload(u);
  renderLog();
  refreshNotify();
}

async function api(method, url, body) {
  const r = await fetch(url, { method, headers: body === undefined ? {} : { "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (r.status === 401 && !url.startsWith("/api/auth/")) { showAuth(); throw Object.assign(new Error(t("ui.sessionExpired")), { status: 401, code: "SESSION_EXPIRED", messageData: message("ui.sessionExpired") }); }
  const text = await r.text();
  const data = text && (r.headers.get("content-type") || "").includes("json") ? JSON.parse(text) : text;
  if (!r.ok) throw Object.assign(new Error((data && data.error) || r.statusText), { status: r.status, data });
  return data;
}

// ---------- entrar ----------
let setupMode = false;
async function boot() {
  await loadTranslations();
  const st = await api("GET", "/api/auth/state");
  setLanguage(st.language || "auto");
  if (st.user) return start(st.user);
  showAuth(st.setup);
}

function showAuth(setup = setupMode) {
  setupMode = setup;
  events?.close(); events = null;
  $("#shell").hidden = true;
  $("#auth").hidden = false;
  $("#authTitle").textContent = t(setup ? "ui.createAccess" : "ui.login");
  $("#authHint").textContent = setup ? t("ui.setupHint") : "";
  $("#authBtn").textContent = t(setup ? "ui.createSignIn" : "ui.login");
  $("#authPass").autocomplete = setup ? "new-password" : "current-password";
  bindText($("#authError"), "");
  $("#authUser").focus();
}

$("#authForm").addEventListener("submit", async e => {
  e.preventDefault();
  bindText($("#authError"), "");
  $("#authBtn").disabled = true;
  try {
    const user = $("#authUser").value.trim();
    await api("POST", setupMode ? "/api/auth/setup" : "/api/auth/login", { user, password: $("#authPass").value });
    $("#authPass").value = "";
    start(user);
  } catch (err) { bindText($("#authError"), errorMessage(err)); }
  finally { $("#authBtn").disabled = false; }
});

// ---------- casca ----------
let events = null;
let settings = {};
async function start(user) {
  $("#auth").hidden = true;
  $("#shell").hidden = false;
  $("#whoami").textContent = user;
  settings = await api("GET", "/api/settings");
  setLanguage(settings.language || "auto");
  fillSettings();
  const logs = await api("GET", "/api/log");
  logRows = Array.isArray(logs) ? logs : String(logs).split("\n").filter(Boolean);
  renderLog();
  connect();
  refreshNotify();
}

function connect() {
  events?.close();
  events = new EventSource("/api/events");
  events.addEventListener("state", e => render(JSON.parse(e.data)));
  events.addEventListener("log", e => appendLog(JSON.parse(e.data)));
  events.addEventListener("notice", e => notice(JSON.parse(e.data)));
  // caiu: o EventSource reconecta sozinho; se foi o login que expirou, volta para a tela de entrar
  events.onerror = async () => { try { const st = await api("GET", "/api/auth/state"); if (!st.user) showAuth(st.setup); } catch { } };
}

$$(".nav").forEach(b => b.addEventListener("click", () => showPage(b.dataset.page)));
function showPage(page) {
  $$(".nav").forEach(b => b.classList.toggle("on", b.dataset.page === page));
  $$(".page").forEach(p => p.hidden = p.id !== "page-" + page);
  if (page === "settings") reloadSettings(); // a Engine pode ter aprendido senhas
  if (page === "log") $("#logBox").scrollTop = $("#logBox").scrollHeight;
}

// ---------- fila ----------
const rows = new Map();
let lastState = null;

function render(s, acceptLanguage = true) {
  lastState = s;
  if (acceptLanguage && s.language && !edits.language && s.language !== languageMode) {
    setLanguage(s.language);
    const select = $('[data-set="language"]');
    if (document.activeElement !== select) select.value = s.language;
    return;
  }
  const box = $("#jobs");
  const seen = new Set();
  s.jobs.forEach((j, i) => {
    seen.add(j.id);
    let el = rows.get(j.id);
    if (!el) { el = newRow(j.id); rows.set(j.id, el); }
    fillRow(el, j);
    if (box.children[i] !== el) box.insertBefore(el, box.children[i] || null);
  });
  for (const [id, el] of rows) if (!seen.has(id)) { el.remove(); rows.delete(id); }

  const m = s.summary, parts = [];
  if (m.sending) parts.push(t("ui.sending", fmt(m.sending, 0)));
  if (m.queued) parts.push(t("ui.queued", fmt(m.queued, 0)));
  if (m.done) parts.push(t(m.done === 1 ? "ui.doneOne" : "ui.doneMany", fmt(m.done, 0)));
  if (m.requested) parts.push(t("ui.installationsRequested", fmt(m.requested, 0)));
  if (m.ready) parts.push(t("ui.packagesReady", fmt(m.ready, 0)));
  if (m.submitting) parts.push(t("ui.installationsSubmitting", fmt(m.submitting, 0)));
  if (m.unknown) parts.push(t("ui.installationsUnknown", fmt(m.unknown, 0)));
  if (m.errors) parts.push(t(m.errors === 1 ? "ui.errorsOne" : "ui.errorsMany", fmt(m.errors, 0)));
  $("#summary").textContent = parts.length ? parts.join(" · ") : t("ui.emptyQueue");
  $("#speed").hidden = !(m.rate > 0);
  if (m.rate > 0) { const [v, u] = size(m.rate); $("#speedVal").textContent = v; $("#speedUnit").textContent = u + "/s"; }
  $("#clearDone").hidden = !(m.done + (m.requested || 0));
  const pending = s.jobs.length - m.done - (m.requested || 0);
  $("#badge").hidden = pending <= 0;
  $("#badge").textContent = pending;
  $("#empty").hidden = s.jobs.length > 0 || uploads.size > 0;
  $("#emptyFolder").textContent = settings.inputFolder ? t("ui.watchFolderPath", settings.inputFolder) : "";
  document.title = m.sending ? `Ferry · ${fmt1.format(s.jobs.find(j => j.isActive)?.progress ?? 0)}%` : "Ferry";

  // cartão do PS5 na barra lateral
  const p = s.ps5;
  $("#dot").className = "dot " + p.status;
  $("#statusTitle").textContent = t({ online: "ui.online", offline: "ui.offline", testing: "ui.testing" }[p.status] || "ui.untested");
  $("#statusHost").textContent = `${p.host}:${p.port}`;
  $("#statusDest").textContent = p.remoteDir;
  $("#found").hidden = !p.found;
  if (p.found) $("#foundText").textContent = t("ui.found", p.found);
  password(s.password);
}

function newRow(id) {
  const el = $("#jobTpl").content.firstElementChild.cloneNode(true);
  el.dataset.id = id;
  translateElements(el);
  $$("[data-act]", el).forEach(b => b.addEventListener("click", async () => {
    const unknown = el.dataset.stage === "VerifiqueNoPs5";
    if (b.dataset.act === "install" && unknown && !confirm(t("ui.installConfirm", $(".title", el).textContent))) return;
    b.disabled = true;
    try { await api("POST", `/api/jobs/${id}/${b.dataset.act}`, b.dataset.act === "install" ? { confirmUnknown: unknown } : undefined); }
    catch (e) { toast("error", message("ui.actionFailed"), errorMessage(e)); }
    finally { b.disabled = false; }
  }));
  return el;
}

function fillRow(el, j) {
  el.dataset.stage = j.stage;
  el.classList.toggle("active", j.isActive);
  el.classList.toggle("nocover", !j.icon);
  const img = $(".cover", el);
  if (j.icon && img.dataset.v !== String(j.icon)) { img.dataset.v = j.icon; img.src = `/api/jobs/${j.id}/icon?v=${j.icon}`; }
  text($(".title", el), j.title || j.name);
  $(".title", el).title = j.title || j.name;
  text($(".stage", el), j.stageMessage ? renderMessage(j.stageMessage) : t("ui.stage." + j.stage));
  text($(".tid", el), j.titleId);
  text($(".package-format", el), j.packageFormat);
  text($(".amount", el), j.totalBytes > 0 ? t("ui.amount", size(j.doneBytes).join(" "), size(j.totalBytes).join(" ")) : "");
  text($(".detail", el), renderMessage(j.detailMessage || j.detail));
  $(".detail", el).title = renderMessage(j.detailMessage || j.detail);
  text($(".file", el), j.currentFile);
  text($(".pct .v", el), fmt1.format(j.progress));
  const [rv, ru] = j.rate > 0 ? size(j.rate) : ["—", "MB"];
  text($(".rate .v", el), rv); text($(".rate .u", el), ru + "/s");
  const [ev, eu] = remaining(j.secondsRemaining);
  text($(".eta .v", el), ev); text($(".eta .u", el), eu);
  $(".rate", el).hidden = $(".eta", el).hidden = j.canSendNow || j.canRequestInstall;
  $(".pct", el).hidden = !!j.canRequestInstall;
  $(".sendnow", el).hidden = !j.canSendNow;
  $(".install", el).hidden = !j.canRequestInstall;
  text($(".install", el), t(j.stage === "VerifiqueNoPs5" ? "ui.resendInstall" : "ui.requestInstall"));
  const show = { pause: j.canPause, resume: j.canResume, retry: j.canRetry, cancel: j.canCancel, remove: true };
  $$(".acts [data-act]", el).forEach(b => b.hidden = !show[b.dataset.act]);
  $(".fill", el).style.width = j.progress + "%";
}

function text(el, v) { v = v ?? ""; if (el.textContent !== v) el.textContent = v; }

function size(b) {
  if (b >= 2 ** 30) return [fmt(b / 2 ** 30, 2), "GB"];
  if (b >= 2 ** 20) return [fmt(b / 2 ** 20, 1), "MB"];
  if (b >= 1024) return [fmt(b / 1024, 0), "KB"];
  return [fmt(b, 0), "B"];
}
const fmt = (v, d) => v.toLocaleString(locale, { minimumFractionDigits: d, maximumFractionDigits: d });

function remaining(seconds) {
  if (seconds == null || !Number.isFinite(seconds)) return ["—", t("ui.remaining")];
  const value = Math.floor(Math.max(0, Math.min(seconds, 359999)));
  if (value >= 3600) return [`${Math.floor(value / 3600)}:${String(Math.floor(value / 60) % 60).padStart(2, "0")}`, t("ui.hoursRemaining")];
  if (value >= 60) return [`${Math.floor(value / 60)}:${String(value % 60).padStart(2, "0")}`, t("ui.minutesRemaining")];
  return [fmt(value, 0), t("ui.secondsRemaining")];
}

$("#clearDone").addEventListener("click", () => api("POST", "/api/jobs/clear-finished"));

// ---------- senha do arquivo (no lugar do diálogo do app Windows) ----------
let asking = null, answered = 0; // seq do pedido aberto / do último respondido (não reabre enquanto o servidor processa)
function password(p) {
  const dlg = $("#pwDialog");
  if (!p) { if (dlg.open) { asking = null; dlg.close(); } return; }
  if (p.seq <= answered) return;
  $("#pwTitle").textContent = t(p.wrong ? "ui.wrongPassword" : "ui.protected");
  $("#pwTitle").classList.toggle("wrong", p.wrong);
  $("#pwText").textContent = t(p.wrong ? "ui.wrongPasswordHint" : "ui.passwordHint", p.name);
  if (p.seq === asking) return;
  asking = p.seq;
  $("#pwInput").value = "";
  dlg.dataset.id = p.id;
  if (!dlg.open) dlg.showModal();
  $("#pwInput").focus();
}
$("#pwDialog").addEventListener("close", () => {
  const dlg = $("#pwDialog"), ok = dlg.returnValue === "ok";
  if (!asking) return;
  answered = asking;
  asking = null;
  api("POST", `/api/jobs/${dlg.dataset.id}/password`, { password: ok ? $("#pwInput").value : null }).catch(() => { });
});

// ---------- configurações: cada campo salva sozinho ----------
function fillSettings() {
  for (const el of $$("[data-set]")) {
    const k = el.dataset.set, v = settings[k];
    // não atropela o que está sendo digitado nem o que ainda vai ser salvo (a resposta do GET pode chegar depois)
    if (document.activeElement === el || edits[k]) continue;
    if (el.type === "checkbox") el.checked = !!v;
    else if (k === "knownPasswords") el.value = (v || []).join("\n");
    else el.value = v ?? "";
    el.classList.remove("bad");
    const hint = $(`[data-err="${k}"]`);
    if (hint) bindText(hint, "");
  }
  if (!edits.connections) $("#connVal").textContent = settings.connections;
  if (!edits.remoteDir) {
    const opt = [...$("#preset").options].find(o => o.value === settings.remoteDir);
    $("#preset").value = opt ? opt.value : "";
  }
}

const timers = {}, edits = {}; // edits[k]: nº da última edição ainda não salva (0/undefined = nada pendente)
let editSeq = 0;
let settingsWrite = Promise.resolve(true);
function queueSave(k, el) {
  settingsWrite = settingsWrite.then(() => save(k, el));
  return settingsWrite;
}

// Resposta que chega depois de uma edição é velha: descarta (o PUT de cada campo já devolve as configurações atuais).
async function reloadSettings() {
  const at = editSeq;
  const s = await api("GET", "/api/settings");
  if (editSeq !== at) return;
  settings = s;
  if (!edits.language) setLanguage(settings.language || "auto");
  fillSettings();
}
for (const el of $$("[data-set]")) {
  const k = el.dataset.set;
  const ev = el.type === "checkbox" || el.type === "range" || el.tagName === "SELECT" ? "change" : "input";
  el.addEventListener(ev, () => {
    if (k === "connections") $("#connVal").textContent = el.value;
    edits[k] = ++editSeq;
    if (k === "language") setLanguage(el.value);
    clearTimeout(timers[k]);
    timers[k] = setTimeout(() => queueSave(k, el), el.tagName === "TEXTAREA" || el.type === "text" || el.type === "password" || !el.type ? 500 : 0);
    if (k.startsWith("webhook")) bindText($("#webhookTestResult"), "");
  });
  if (el.type === "range") el.addEventListener("input", () => $("#connVal").textContent = el.value);
}

async function save(k, el) {
  const mine = edits[k];
  let v = el.type === "checkbox" ? el.checked : el.value;
  if (k === "port" || k === "dpiPort" || k === "connections") v = /^\d+$/.test(v) ? Number(v) : -1;
  if (k === "knownPasswords") v = el.value.split("\n").map(s => s.trim()).filter(Boolean);
  try {
    const body = { [k]: v };
    if (k.startsWith("webhook")) {
      // Save these together: clicking Enable must include a URL whose debounce is still pending.
      body.webhookEnabled = $('[data-set="webhookEnabled"]').checked;
      body.webhookKind = $('[data-set="webhookKind"]').value;
      body.webhookUrl = $('[data-set="webhookUrl"]').value.trim();
    }
    if (k.startsWith("webhook") || k === "language") body.webhookAutoLocale = resolvedLocale(languageMode);
    const r = await api("PUT", "/api/settings", body);
    const err = r.errorsMessages?.[k] || r.errors[k] || "";
    el.classList.toggle("bad", !!err);
    const hint = $(`[data-err="${k}"]`);
    if (hint) bindText(hint, err);
    if (k.startsWith("webhook")) {
      for (const key of ["webhookEnabled", "webhookKind", "webhookUrl"]) {
        const problem = r.errorsMessages?.[key] || r.errors[key] || "";
        $(`[data-set="${key}"]`).classList.toggle("bad", !!problem);
        bindText($(`[data-err="${key}"]`), problem);
      }
    }
    settings = r.settings;
    if (k === "language" && edits[k] === mine) {
      if (lastState) lastState.language = settings.language || "auto";
      setLanguage(settings.language || "auto");
    }
    if (k === "remoteDir") $("#preset").value = [...$("#preset").options].some(o => o.value === v) ? v : "";
    return Object.keys(r.errors).length === 0;
  } catch (e) { toast("error", message("ui.saveFailed"), errorMessage(e)); return false; }
  finally { if (edits[k] === mine) delete edits[k]; } // editou de novo enquanto salvava: continua pendente
}

$("#dpiTestBtn").addEventListener("click", async () => {
  const button = $("#dpiTestBtn"), result = $("#dpiTestResult");
  button.disabled = true;
  result.className = "test-result";
  bindText(result, message("ui.dpiTesting"));
  try {
    let saved = true;
    for (const key of ["host", "dpiPort"]) {
      clearTimeout(timers[key]);
      saved = await queueSave(key, $(`[data-set="${key}"]`)) && saved;
    }
    if (!saved || $('[data-set="host"]').classList.contains("bad") || $('[data-set="dpiPort"]').classList.contains("bad")) {
      bindText(result, message("ui.dpiSaveFirst")); return;
    }
    const response = await api("POST", "/api/ps5/dpi/test");
    result.classList.add(response.ok ? "ok" : "fail");
    bindText(result, response.messageData || response.message);
  } catch (error) { result.classList.add("fail"); bindText(result, errorMessage(error)); }
  finally { button.disabled = false; }
});

$("#webhookTestBtn").addEventListener("click", async () => {
  const button = $("#webhookTestBtn"), result = $("#webhookTestResult");
  button.disabled = true;
  result.classList.remove("error");
  bindText(result, message("ui.webhookTesting"));
  try {
    // Flush edits before testing so a pasted URL is never tested as the previous destination.
    for (const key of ["webhookUrl", "webhookKind", "webhookEnabled", "language"]) {
      if (edits[key]) { clearTimeout(timers[key]); queueSave(key, $(`[data-set="${key}"]`)); }
    }
    const saved = await queueSave("webhookUrl", $('[data-set="webhookUrl"]'));
    if (!saved || $$('[data-set^="webhook"].bad').length) {
      bindText(result, message("ui.webhookSaveFirst"));
      return;
    }
    const response = await api("POST", "/api/webhook/test");
    result.classList.toggle("error", !response.ok);
    bindText(result, response.messageData);
  } catch (error) { result.classList.add("error"); bindText(result, errorMessage(error)); }
  finally { button.disabled = false; }
});

$("#preset").addEventListener("change", () => {
  const v = $("#preset").value;
  if (!v) return;
  const el = $('[data-set="remoteDir"]');
  el.value = v;
  queueSave("remoteDir", el);
});

async function test() {
  $("#testBtn").disabled = true;
  $("#testResult").className = "test-result";
  bindText($("#testResult"), message("ui.testing"));
  try {
    const r = await api("POST", "/api/ps5/test");
    bindText($("#testResult"), r.messageData || r.message);
    $("#testResult").classList.add(r.ok ? "ok" : "fail");
  } finally { $("#testBtn").disabled = false; }
}
$("#testBtn").addEventListener("click", test);
$("#sideTest").addEventListener("click", test);
$("#findBtn").addEventListener("click", async () => {
  $("#findBtn").disabled = true;
  $("#testResult").className = "test-result";
  bindText($("#testResult"), message("ui.searching"));
  try { const r = await api("POST", "/api/ps5/discover"); bindText($("#testResult"), r.messageData || r.message); }
  finally { $("#findBtn").disabled = false; }
});
$("#useFound").addEventListener("click", async () => {
  await api("POST", "/api/ps5/use-found");
  reloadSettings();
});
$("#dismissFound").addEventListener("click", () => api("POST", "/api/ps5/dismiss-found"));
$("#logout").addEventListener("click", async () => { await api("POST", "/api/auth/logout"); showAuth(false); });

// ---------- log ----------
let logRows = [];
function renderLog() {
  const box = $("#logBox"), atEnd = box.scrollHeight - box.scrollTop - box.clientHeight < 40;
  box.textContent = logRows.map(row => typeof row === "string" ? row : `${row.time}  ${renderMessage(row.message)}`).join("\n") + (logRows.length ? "\n" : "");
  if (atEnd) box.scrollTop = box.scrollHeight;
}
function appendLog(line) {
  if (line.seq != null && logRows.some(row => row.seq === line.seq)) return;
  logRows.push(line);
  if (logRows.length > 4000) logRows = logRows.slice(-2000);
  renderLog();
}
$("#clearLog").addEventListener("click", async () => { await api("POST", "/api/log/clear"); logRows = []; renderLog(); });
$("#copyLog").addEventListener("click", async () => {
  const t = $("#logBox").textContent;
  try { await navigator.clipboard.writeText(t); } // só em https/localhost
  catch { const r = document.createRange(); r.selectNodeContents($("#logBox")); getSelection().removeAllRanges(); getSelection().addRange(r); document.execCommand("copy"); }
  toast("done", message("ui.logCopied"), "");
});

// ---------- avisos: na página sempre; do sistema quando o navegador deixa (https ou localhost) ----------
function notice(n) {
  const title = n.titleMessage || n.title, body = n.textMessage || n.text;
  toast(n.kind, title, body);
  if (document.hidden && "Notification" in window && Notification.permission === "granted") new Notification(renderMessage(title), { body: renderMessage(body), icon: "logo.svg" });
}
function toast(kind, title, body) {
  const t = document.createElement("div");
  t.className = "toast " + kind;
  const b = document.createElement("b"); bindText(b, title); t.append(b);
  const detail = document.createElement("span"); bindText(detail, body); t.append(detail);
  $("#toasts").append(t);
  setTimeout(() => { localizedText.delete(b); localizedText.delete(detail); t.remove(); }, 6000);
}
function refreshNotify() {
  const can = "Notification" in window && window.isSecureContext;
  $("#notifyBtn").hidden = !can || Notification.permission !== "default";
  $("#notifyText").textContent = t(!can ? "ui.notifyInsecure" : Notification.permission === "granted" ? "ui.notifyGranted" : Notification.permission === "denied" ? "ui.notifyDenied" : "ui.notifyDefault");
}
$("#notifyBtn").addEventListener("click", async () => { await Notification.requestPermission(); refreshNotify(); });

// ---------- enviar arquivos pelo navegador: blocos de 16 MB, continua de onde parou ----------
const CHUNK = 16 << 20;
const uploads = new Map();
let uploading = Promise.resolve();

$("#addBtn").addEventListener("click", () => $("#fileInput").click());
$("#empty").addEventListener("click", () => $("#fileInput").click());
$("#fileInput").addEventListener("change", e => { addUploads([...e.target.files]); e.target.value = ""; });

let dragDepth = 0;
const hasFiles = e => [...(e.dataTransfer?.types || [])].includes("Files");
addEventListener("dragenter", e => { if (!hasFiles(e) || $("#shell").hidden) return; dragDepth++; showPage("queue"); $("#drop").hidden = false; });
addEventListener("dragleave", () => { if (--dragDepth <= 0) { dragDepth = 0; $("#drop").hidden = true; } });
addEventListener("dragover", e => { if (hasFiles(e)) e.preventDefault(); });
addEventListener("drop", e => {
  if (!hasFiles(e)) return;
  e.preventDefault(); dragDepth = 0; $("#drop").hidden = true;
  if (!$("#shell").hidden) addUploads([...e.dataTransfer.files]);
});
addEventListener("beforeunload", e => { if ([...uploads.values()].some(u => !u.done)) e.preventDefault(); });

function addUploads(files) {
  for (const f of files) {
    const key = `${f.name}|${f.size}|${f.lastModified}`;
    if (uploads.has(key)) continue;
    const el = document.createElement("div");
    el.className = "upload";
    el.innerHTML = `<b></b><span class="up-num"></span><span class="up-detail"></span><span></span><div class="up-bar"><i style="width:0"></i></div>`;
    $("b", el).textContent = f.name;
    $("#uploads").append(el);
    const u = { f, el, done: false, off: 0, mode: "queued" };
    renderUpload(u);
    uploads.set(key, u);
    uploading = uploading.then(() => upload(u)).catch(() => { });
  }
  if (lastState) render(lastState);
}

async function upload(u) {
  const { f, el } = u;
  const show = (off, mode, rate = 0, error = "") => {
    Object.assign(u, { off, mode, rate, error });
    renderUpload(u);
  };
  for (let wait = 1000; ;) {
    try {
      const s = await api("POST", "/api/uploads", { name: f.name, size: f.size, lastModified: f.lastModified });
      let off = s.offset, t0 = performance.now(), sent = 0;
      while (!s.done && off < f.size) {
        const r = await fetch(`/api/uploads/${s.id}?offset=${off}`, { method: "PUT", headers: { "Content-Type": "application/octet-stream" }, body: f.slice(off, off + CHUNK) });
        if (r.status === 401) { showAuth(); throw Object.assign(new Error(t("ui.sessionExpired")), { status: 401, code: "SESSION_EXPIRED", messageData: message("ui.sessionExpired") }); }
        const j = await r.json().catch(() => ({}));
        if (r.status === 409) { off = j.offset; continue; }
        if (!r.ok) throw Object.assign(new Error(j.error || r.statusText), { fatal: r.status === 400, data: j });
        sent += j.offset - off; off = j.offset; wait = 1000;
        const rate = sent / ((performance.now() - t0) / 1000);
        show(off, "sending", rate);
      }
      u.done = true;
      show(f.size, "done");
      setTimeout(() => { el.remove(); uploads.delete(`${f.name}|${f.size}|${f.lastModified}`); if (lastState) render(lastState); }, 4000);
      return;
    } catch (e) {
      if (e.fatal || e.status === 401 || e.code === "SESSION_EXPIRED") { el.classList.add("fail"); show(u.off, "failed", 0, errorMessage(e)); u.done = true; return; }
      // rede caiu ou servidor reiniciou: tenta de novo e continua do byte em que o servidor parou
      el.classList.add("fail");
      show(u.off, "retry", 0, errorMessage(e));
      await new Promise(r => setTimeout(r, wait));
      wait = Math.min(wait * 2, 30000);
      el.classList.remove("fail");
    }
  }
}

function renderUpload(u) {
  const progress = u.f.size ? u.off * 100 / u.f.size : 100;
  $(".up-num", u.el).textContent = fmt1.format(progress) + "%";
  $(".up-bar i", u.el).style.width = progress + "%";
  const detail = u.mode === "sending" ? message("ui.uploadSending", size(u.off).join(" "), size(u.f.size).join(" "), size(u.rate).join(" "))
    : u.mode === "failed" ? message("ui.uploadFailed", u.error)
    : u.mode === "retry" ? message("ui.uploadRetry", u.error)
    : message(u.mode === "done" ? "ui.uploadDone" : "ui.uploadQueued");
  $(".up-detail", u.el).textContent = renderMessage(detail);
}

boot().catch(e => { document.body.textContent = "Ferry: " + e.message; });
