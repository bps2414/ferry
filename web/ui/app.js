// Ferry web: fila ao vivo por SSE, configurações salvas campo a campo, upload em blocos com retomada.
"use strict";
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const fmt1 = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

async function api(method, url, body) {
  const r = await fetch(url, { method, headers: body === undefined ? {} : { "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (r.status === 401 && !url.startsWith("/api/auth/")) { showAuth(); throw new Error("sessão expirada"); }
  const text = await r.text();
  const data = text && (r.headers.get("content-type") || "").includes("json") ? JSON.parse(text) : text;
  if (!r.ok) throw Object.assign(new Error((data && data.error) || r.statusText), { status: r.status, data });
  return data;
}

// ---------- entrar ----------
let setupMode = false;
async function boot() {
  const st = await api("GET", "/api/auth/state");
  if (st.user) return start(st.user);
  showAuth(st.setup);
}

function showAuth(setup = setupMode) {
  setupMode = setup;
  events?.close(); events = null;
  $("#shell").hidden = true;
  $("#auth").hidden = false;
  $("#authTitle").textContent = setup ? "Criar acesso" : "Entrar";
  $("#authHint").textContent = setup ? "Primeira abertura: escolha o usuário e a senha (8 ou mais caracteres) para abrir o Ferry deste servidor." : "";
  $("#authBtn").textContent = setup ? "Criar e entrar" : "Entrar";
  $("#authPass").autocomplete = setup ? "new-password" : "current-password";
  $("#authError").textContent = "";
  $("#authUser").focus();
}

$("#authForm").addEventListener("submit", async e => {
  e.preventDefault();
  $("#authError").textContent = "";
  $("#authBtn").disabled = true;
  try {
    const user = $("#authUser").value.trim();
    await api("POST", setupMode ? "/api/auth/setup" : "/api/auth/login", { user, password: $("#authPass").value });
    $("#authPass").value = "";
    start(user);
  } catch (err) { $("#authError").textContent = err.message; }
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
  fillSettings();
  $("#logBox").textContent = await api("GET", "/api/log");
  $("#logBox").textContent += $("#logBox").textContent ? "\n" : "";
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

function render(s) {
  lastState = s;
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
  if (m.sending) parts.push(`${m.sending} enviando`);
  if (m.queued) parts.push(`${m.queued} na fila`);
  if (m.done) parts.push(`${m.done} concluído(s)`);
  if (m.errors) parts.push(`${m.errors} com erro`);
  $("#summary").textContent = parts.length ? parts.join(" · ") : "Nenhum jogo na fila";
  $("#speed").hidden = !(m.rate > 0);
  if (m.rate > 0) { const [v, u] = size(m.rate); $("#speedVal").textContent = v; $("#speedUnit").textContent = u + "/s"; }
  $("#clearDone").hidden = !m.done;
  const pending = s.jobs.length - m.done;
  $("#badge").hidden = pending <= 0;
  $("#badge").textContent = pending;
  $("#empty").hidden = s.jobs.length > 0 || uploads.size > 0;
  $("#emptyFolder").textContent = settings.inputFolder ? `Ou coloque os arquivos na pasta monitorada do servidor: ${settings.inputFolder}` : "";
  document.title = m.sending ? `Ferry · ${fmt1.format(s.jobs.find(j => j.isActive)?.progress ?? 0)}%` : "Ferry";

  // cartão do PS5 na barra lateral
  const p = s.ps5;
  $("#dot").className = "dot " + p.status;
  $("#statusTitle").textContent = { online: "PS5 online", offline: "PS5 offline", testing: "Testando…" }[p.status] || "PS5 não testado";
  $("#statusHost").textContent = `${p.host}:${p.port}`;
  $("#statusDest").textContent = p.remoteDir;
  $("#found").hidden = !p.found;
  if (p.found) $("#foundText").textContent = `Achei um PS5 em ${p.found}.`;
  password(s.password);
}

function newRow(id) {
  const el = $("#jobTpl").content.firstElementChild.cloneNode(true);
  el.dataset.id = id;
  $$("[data-act]", el).forEach(b => b.addEventListener("click", () => api("POST", `/api/jobs/${id}/${b.dataset.act}`).catch(e => toast("error", "Não deu", e.message))));
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
  text($(".stage", el), j.stageText);
  text($(".tid", el), j.titleId);
  text($(".amount", el), j.amount);
  text($(".detail", el), j.detail);
  text($(".file", el), j.currentFile);
  text($(".pct .v", el), fmt1.format(j.progress));
  text($(".rate .v", el), j.rateValue); text($(".rate .u", el), j.rateUnit);
  text($(".eta .v", el), j.etaValue); text($(".eta .u", el), j.etaUnit);
  $(".rate", el).hidden = $(".eta", el).hidden = j.canSendNow;
  $(".sendnow", el).hidden = !j.canSendNow;
  const show = { pause: j.canPause, resume: j.canResume, retry: j.canRetry, cancel: j.canCancel, remove: true };
  $$(".acts [data-act]", el).forEach(b => b.hidden = !show[b.dataset.act]);
  $(".fill", el).style.width = j.progress + "%";
}

function text(el, v) { v = v ?? ""; if (el.textContent !== v) el.textContent = v; }

function size(b) {
  if (b >= 2 ** 30) return [fmt(b / 2 ** 30, 2), "GB"];
  if (b >= 2 ** 20) return [fmt(b / 2 ** 20, 1), "MB"];
  if (b >= 1024) return [fmt(b / 1024, 0), "KB"];
  return [String(b), "B"];
}
const fmt = (v, d) => v.toLocaleString("pt-BR", { minimumFractionDigits: d, maximumFractionDigits: d });

$("#clearDone").addEventListener("click", () => api("POST", "/api/jobs/clear-finished"));

// ---------- senha do arquivo (no lugar do diálogo do app Windows) ----------
let asking = null, answered = 0; // seq do pedido aberto / do último respondido (não reabre enquanto o servidor processa)
function password(p) {
  const dlg = $("#pwDialog");
  if (!p) { if (dlg.open) { asking = null; dlg.close(); } return; }
  if (p.seq === asking || p.seq <= answered) return;
  asking = p.seq;
  $("#pwTitle").textContent = p.wrong ? "Senha incorreta" : "Arquivo protegido por senha";
  $("#pwTitle").classList.toggle("wrong", p.wrong);
  $("#pwText").textContent = p.wrong ? `A senha não abriu "${p.name}". Tente de novo.` : `Digite a senha de "${p.name}".`;
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
    if (hint) hint.textContent = "";
  }
  if (!edits.connections) $("#connVal").textContent = settings.connections;
  if (!edits.remoteDir) {
    const opt = [...$("#preset").options].find(o => o.value === settings.remoteDir);
    $("#preset").value = opt ? opt.value : "";
  }
}

const timers = {}, edits = {}; // edits[k]: nº da última edição ainda não salva (0/undefined = nada pendente)
let editSeq = 0;

// Resposta que chega depois de uma edição é velha: descarta (o PUT de cada campo já devolve as configurações atuais).
async function reloadSettings() {
  const at = editSeq;
  const s = await api("GET", "/api/settings");
  if (editSeq !== at) return;
  settings = s;
  fillSettings();
}
for (const el of $$("[data-set]")) {
  const k = el.dataset.set;
  const ev = el.type === "checkbox" || el.type === "range" ? "change" : "input";
  el.addEventListener(ev, () => {
    if (k === "connections") $("#connVal").textContent = el.value;
    edits[k] = ++editSeq;
    clearTimeout(timers[k]);
    timers[k] = setTimeout(() => save(k, el), el.tagName === "TEXTAREA" || el.type === "text" || el.type === "password" || !el.type ? 500 : 0);
  });
  if (el.type === "range") el.addEventListener("input", () => $("#connVal").textContent = el.value);
}

async function save(k, el) {
  const mine = edits[k];
  let v = el.type === "checkbox" ? el.checked : el.value;
  if (k === "port" || k === "connections") v = /^\d+$/.test(v) ? Number(v) : -1;
  if (k === "knownPasswords") v = el.value.split("\n").map(s => s.trim()).filter(Boolean);
  try {
    const r = await api("PUT", "/api/settings", { [k]: v });
    const err = r.errors[k] || "";
    el.classList.toggle("bad", !!err);
    const hint = $(`[data-err="${k}"]`);
    if (hint) hint.textContent = err;
    settings = r.settings;
    if (k === "remoteDir") $("#preset").value = [...$("#preset").options].some(o => o.value === v) ? v : "";
  } catch (e) { toast("error", "Não salvou", e.message); }
  finally { if (edits[k] === mine) delete edits[k]; } // editou de novo enquanto salvava: continua pendente
}

$("#preset").addEventListener("change", () => {
  const v = $("#preset").value;
  if (!v) return;
  const el = $('[data-set="remoteDir"]');
  el.value = v;
  save("remoteDir", el);
});

async function test() {
  $("#testBtn").disabled = true;
  $("#testResult").className = "test-result";
  $("#testResult").textContent = "Testando…";
  try {
    const r = await api("POST", "/api/ps5/test");
    $("#testResult").textContent = r.message;
    $("#testResult").classList.add(r.ok ? "ok" : "fail");
  } finally { $("#testBtn").disabled = false; }
}
$("#testBtn").addEventListener("click", test);
$("#sideTest").addEventListener("click", test);
$("#findBtn").addEventListener("click", async () => {
  $("#findBtn").disabled = true;
  $("#testResult").className = "test-result";
  $("#testResult").textContent = "Procurando o PS5 na rede…";
  try { $("#testResult").textContent = (await api("POST", "/api/ps5/discover")).message; }
  finally { $("#findBtn").disabled = false; }
});
$("#useFound").addEventListener("click", async () => {
  await api("POST", "/api/ps5/use-found");
  reloadSettings();
});
$("#dismissFound").addEventListener("click", () => api("POST", "/api/ps5/dismiss-found"));
$("#logout").addEventListener("click", async () => { await api("POST", "/api/auth/logout"); showAuth(false); });

// ---------- log ----------
function appendLog(line) {
  const box = $("#logBox"), atEnd = box.scrollHeight - box.scrollTop - box.clientHeight < 40;
  box.append(line + "\n");
  if (box.childNodes.length > 4000) box.textContent = box.textContent.split("\n").slice(-2000).join("\n");
  if (atEnd) box.scrollTop = box.scrollHeight;
}
$("#clearLog").addEventListener("click", async () => { await api("POST", "/api/log/clear"); $("#logBox").textContent = ""; });
$("#copyLog").addEventListener("click", async () => {
  const t = $("#logBox").textContent;
  try { await navigator.clipboard.writeText(t); } // só em https/localhost
  catch { const r = document.createRange(); r.selectNodeContents($("#logBox")); getSelection().removeAllRanges(); getSelection().addRange(r); document.execCommand("copy"); }
  toast("done", "Log copiado", "");
});

// ---------- avisos: na página sempre; do sistema quando o navegador deixa (https ou localhost) ----------
function notice(n) {
  toast(n.kind, n.title, n.text);
  if (document.hidden && "Notification" in window && Notification.permission === "granted") new Notification(n.title, { body: n.text, icon: "logo.svg" });
}
function toast(kind, title, body) {
  const t = document.createElement("div");
  t.className = "toast " + kind;
  const b = document.createElement("b"); b.textContent = title; t.append(b);
  if (body) t.append(body);
  $("#toasts").append(t);
  setTimeout(() => t.remove(), 6000);
}
function refreshNotify() {
  const can = "Notification" in window && window.isSecureContext;
  $("#notifyBtn").hidden = !can || Notification.permission !== "default";
  $("#notifyText").textContent = !can ? "Avisos do sistema precisam de https (ou localhost); os avisos aparecem aqui na página."
    : Notification.permission === "granted" ? "Avisos do sistema ativados." : Notification.permission === "denied" ? "Avisos do sistema bloqueados no navegador." : "Avisos do sistema quando a aba estiver em segundo plano.";
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
    el.innerHTML = `<b></b><span class="up-num"></span><span class="up-detail">Na fila de envio ao servidor</span><span></span><div class="up-bar"><i style="width:0"></i></div>`;
    $("b", el).textContent = f.name;
    $("#uploads").append(el);
    const u = { f, el, done: false };
    uploads.set(key, u);
    uploading = uploading.then(() => upload(u)).catch(() => { });
  }
  if (lastState) render(lastState);
}

async function upload(u) {
  const { f, el } = u;
  const show = (off, detail) => {
    $(".up-num", el).textContent = fmt1.format(f.size ? off * 100 / f.size : 100) + "%";
    $(".up-bar i", el).style.width = (f.size ? off * 100 / f.size : 100) + "%";
    $(".up-detail", el).textContent = detail;
  };
  for (let wait = 1000; ;) {
    try {
      const s = await api("POST", "/api/uploads", { name: f.name, size: f.size, lastModified: f.lastModified });
      let off = s.offset, t0 = performance.now(), sent = 0;
      while (!s.done && off < f.size) {
        const r = await fetch(`/api/uploads/${s.id}?offset=${off}`, { method: "PUT", headers: { "Content-Type": "application/octet-stream" }, body: f.slice(off, off + CHUNK) });
        if (r.status === 401) { showAuth(); throw new Error("sessão expirada"); }
        const j = await r.json().catch(() => ({}));
        if (r.status === 409) { off = j.offset; continue; }
        if (!r.ok) throw Object.assign(new Error(j.error || r.statusText), { fatal: r.status === 400 });
        sent += j.offset - off; off = j.offset; wait = 1000;
        const rate = sent / ((performance.now() - t0) / 1000);
        const [rv, ru] = size(rate), [dv, du] = size(off), [tv, tu] = size(f.size);
        show(off, `Enviando ao servidor · ${dv} ${du} de ${tv} ${tu} · ${rv} ${ru}/s`);
      }
      u.done = true;
      show(f.size, "No servidor. Entra na fila quando todas as partes chegarem.");
      setTimeout(() => { el.remove(); uploads.delete(`${f.name}|${f.size}|${f.lastModified}`); if (lastState) render(lastState); }, 4000);
      return;
    } catch (e) {
      if (e.fatal || e.message === "sessão expirada") { el.classList.add("fail"); $(".up-detail", el).textContent = "Falhou: " + e.message; u.done = true; return; }
      // rede caiu ou servidor reiniciou: tenta de novo e continua do byte em que o servidor parou
      el.classList.add("fail");
      $(".up-detail", el).textContent = `Sem conexão com o servidor, tentando de novo… (${e.message})`;
      await new Promise(r => setTimeout(r, wait));
      wait = Math.min(wait * 2, 30000);
      el.classList.remove("fail");
    }
  }
}

boot().catch(e => { document.body.textContent = "Ferry: " + e.message; });
