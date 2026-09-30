// E2E da versão self-hosted: sobe a imagem Docker do Ferry (rede do host, usuário comum), um FTP falso imitando o
// ftpsrv do PS5 (e2e/ftpserver.py) e dirige a interface web num Chromium de verdade (Playwright).
// Gera e2e_report_web.md na raiz e as capturas em e2e/web/report/. Sai com 0 (passou) ou 1 (falhou).
//
// Uso: docker build -t ferry:e2e . && (cd e2e/web && npm ci && node web.mjs)
// Requer: Docker, Python 3 com pyftpdlib, 7-Zip (7zz ou 7z) no PATH.
import { chromium } from "playwright";
import { spawn, execFileSync } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import zlib from "node:zlib";

const root = path.resolve(import.meta.dirname, "..", "..");
const image = process.env.FERRY_IMAGE || "ferry:e2e";
const work = path.join(os.tmpdir(), "ferry-web-e2e");
const shots = path.join(root, "e2e", "web", "report");
const name = "ferry-web-e2e";
const t0 = Date.now();
fs.rmSync(work, { recursive: true, force: true });
fs.rmSync(shots, { recursive: true, force: true });
const dir = (...p) => fs.mkdirSync(path.join(work, ...p), { recursive: true }) ?? path.join(work, ...p);
const [data, games, ftpRoot, src] = [dir("data"), dir("games"), dir("ftproot"), dir("src")];
fs.mkdirSync(shots, { recursive: true });

const results = [];
function check(what, ok, detail) {
  results.push({ what, ok: !!ok, detail });
  console.log(`${ok ? "OK  " : "FALHA"} ${what}${detail ? " — " + detail : ""}`);
}
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function until(fn, ms, what) {
  const end = Date.now() + ms;
  for (; ;) {
    try { const v = await fn(); if (v) return v; } catch { }
    if (Date.now() > end) throw new Error("tempo esgotado esperando: " + what);
    await sleep(100);
  }
}
const sha = f => crypto.createHash("sha256").update(fs.readFileSync(f)).digest("hex");
const freePort = () => new Promise(r => { const s = net.createServer().listen(0, "127.0.0.1", () => { const p = s.address().port; s.close(() => r(p)); }); });
const sevenZip = ["7zz", "7z"].find(e => { try { execFileSync("which", [e], { stdio: "ignore" }); return true; } catch { return false; } });

// ---------- jogo falso: pasta com EBOOT.BIN + sce_sys (param.sfo real, icon0.png) dentro de uma casca ----------
function sfo(kv) {
  const idx = [], keys = [], vals = [];
  let kLen = 0, dLen = 0;
  for (const [k, v] of kv) {
    const vb = Buffer.from(v + "\0", "utf8"), max = Math.ceil(vb.length / 4) * 4;
    const e = Buffer.alloc(16);
    e.writeUInt16LE(kLen, 0); e.writeUInt16LE(0x0204, 2); e.writeUInt32LE(vb.length, 4); e.writeUInt32LE(max, 8); e.writeUInt32LE(dLen, 12);
    idx.push(e); keys.push(Buffer.from(k + "\0", "ascii")); vals.push(Buffer.concat([vb, Buffer.alloc(max - vb.length)]));
    kLen += k.length + 1; dLen += max;
  }
  let keyBuf = Buffer.concat(keys);
  keyBuf = Buffer.concat([keyBuf, Buffer.alloc((4 - keyBuf.length % 4) % 4)]);
  const keyStart = 20 + 16 * kv.length, h = Buffer.alloc(20);
  h.write("\0PSF", 0, "latin1"); h.writeUInt32LE(0x101, 4); h.writeUInt32LE(keyStart, 8); h.writeUInt32LE(keyStart + keyBuf.length, 12); h.writeUInt32LE(kv.length, 16);
  return Buffer.concat([h, ...idx, keyBuf, ...vals]);
}
// PNG 64x64 de ruído (zlib do Node + CRC do próprio PNG)
function png(seed) {
  const W = 64, raw = Buffer.alloc(W * (1 + W * 4));
  let x = seed;
  for (let i = 0; i < raw.length; i++) raw[i] = i % (1 + W * 4) === 0 ? 0 : (x = (x * 1103515245 + 12345) >>> 0) >>> 24;
  const crcT = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = b => { let c = 0xffffffff; for (const v of b) c = crcT[(c ^ v) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (t, d) => { const td = Buffer.concat([Buffer.from(t, "ascii"), d]), l = Buffer.alloc(4), c = Buffer.alloc(4); l.writeUInt32BE(d.length); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(W, 0); ihdr.writeUInt32BE(W, 4); ihdr.set([8, 6, 0, 0, 0], 8);
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

const game = "PPSA09001-Jogo Web";
const gameDir = path.join(src, "W1", "Casca", game);
const put = (rel, buf) => { const p = path.join(gameDir, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, buf); };
put("EBOOT.BIN", crypto.randomBytes(1_200_000));
put("sce_sys/param.sfo", sfo([["TITLE", "Jogo Web"], ["TITLE_ID", "PPSA09001"]]));
put("sce_sys/param.json", Buffer.from('{"titleId":"PPSA09001"}'));
put("sce_sys/icon0.png", png(9001));
put("data/big.bin", crypto.randomBytes(120_000_000));
put("data/sub pasta/ação çõ.dat", crypto.randomBytes(700_000));
put("data/vazio.bin", Buffer.alloc(0));
const archDir = path.join(src, "arch");
fs.mkdirSync(archDir);
// .7z.001… com senha e cabeçalhos cifrados: o diálogo de senha aparece no navegador
execFileSync(sevenZip, ["a", "-t7z", "-mx0", "-v16m", "-pwebpass", "-mhe=on", path.join(archDir, "W1.7z"), "Casca"], { cwd: path.join(src, "W1"), stdio: "ignore", env: { ...process.env, LC_ALL: "C.UTF-8" } });
const parts = fs.readdirSync(archDir).sort().map(f => path.join(archDir, f));
const imgw = path.join(src, "IMGW.exfat");
fs.writeFileSync(imgw, crypto.randomBytes(400_000_000));
const up = path.join(src, "UP.exfat");
fs.writeFileSync(up, crypto.randomBytes(64_000_000));

// ---------- FTP falso (com APPE, como o ftpsrv novo) e o container ----------
const ftpPort = await freePort(), webPort = await freePort();
const ftpLog = [];
const ftp = spawn("python3", [path.join(root, "e2e", "ftpserver.py"), String(ftpPort), ftpRoot, "appe"]);
ftp.stdout.on("data", d => ftpLog.push(...d.toString().split("\n")));
ftp.stderr.on("data", d => ftpLog.push(...d.toString().split("\n")));
await until(() => new Promise(r => { const s = net.connect(ftpPort, "127.0.0.1", () => { s.end(); r(true); }).on("error", () => r(false)); }), 10000, "FTP falso");

const docker = (...a) => execFileSync("docker", a, { encoding: "utf8" }).trim();
try { docker("rm", "-f", name); } catch { }
docker("run", "-d", "--name", name, "--network", "host", "--user", `${process.getuid()}:${process.getgid()}`, "-e", "HOME=/tmp",
  "-e", `FERRY_PORT=${webPort}`, "-v", `${data}:/data`, "-v", `${games}:/games`, image);
const base = `http://127.0.0.1:${webPort}`;
const up200 = async () => (await fetch(base + "/")).ok;
await until(up200, 30000, "servidor web do container");

const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, acceptDownloads: false });
const page = await ctx.newPage();
const pageErrors = [];
page.on("pageerror", e => pageErrors.push(e.message));
const shot = async n => { await page.screenshot({ path: path.join(shots, n + ".png") }); return `e2e/web/report/${n}.png`; };
const row = t => page.locator(".job", { has: page.locator(".title", { hasText: t }) });
const stage = async t => (await row(t).count()) ? row(t).getAttribute("data-stage") : null;
const pct = async t => Number((await row(t).locator(".pct .v").textContent()).replace(",", "."));
const getSettings = async () => (await page.request.get(base + "/api/settings")).json();
const shotsTaken = {};
let failed = null;

try {
  // ---------- 1. sem login: API fechada, página pede para criar o acesso ----------
  const anon = await (await fetch(base + "/api/settings")).status;
  const anonEvents = await (await fetch(base + "/api/events")).status;
  check("API sem login responde 401", anon === 401 && anonEvents === 401, `settings ${anon}, events ${anonEvents}`);
  await page.goto(base);
  await page.locator("#auth").waitFor();
  const title = await page.locator("#authTitle").textContent();
  shotsTaken.criar = await shot("01-criar-acesso");
  await page.fill("#authUser", "admin");
  await page.fill("#authPass", "curta");
  await page.click("#authBtn");
  const short = await until(async () => (await page.locator("#authError").textContent()) || null, 5000, "erro de senha curta");
  await page.fill("#authPass", "senha-do-e2e");
  await page.click("#authBtn");
  await page.locator("#shell").waitFor();
  check("Primeira abertura cria o usuário", title === "Criar acesso" && short.includes("8 caracteres") && fs.existsSync(path.join(data, "auth.json")),
    `tela "${title}", senha curta: "${short}", auth.json criado`);

  // ---------- 2. configurações salvas campo a campo, com validação ----------
  await page.click('.nav[data-page="settings"]');
  await page.fill('[data-set="host"]', "127.0.0.1");
  await page.fill('[data-set="port"]', "abc");
  const portErr = await until(async () => (await page.locator('[data-err="port"]').textContent()) || null, 5000, "erro da porta");
  const portKept = (await getSettings()).port;
  await page.fill('[data-set="port"]', String(ftpPort));
  await page.fill('[data-set="user"]', "ps5");
  await page.fill('[data-set="password"]', "ps5pass");
  const saved = await until(async () => { const s = await getSettings(); return s.port === ftpPort && s.password === "ps5pass" && s.host === "127.0.0.1" && s.user === "ps5" ? s : null; }, 8000, "configurações salvas");
  check("Configurações salvam sozinhas e recusam o inválido", portErr.includes("1 a 65535") && portKept === 2121 && saved.inputFolder === "/games",
    `porta "abc" → "${portErr}" (continuou ${portKept}); host/porta/usuário/senha salvos; pasta monitorada = ${saved.inputFolder}`);
  await page.click("#testBtn");
  const test = await until(async () => { const t = await page.locator("#testResult").textContent(); return t.startsWith("Conectado") ? t : null; }, 15000, "teste de conexão");
  await until(async () => (await page.locator("#statusTitle").textContent()) === "PS5 online", 5000, "cartão PS5 online");
  shotsTaken.config = await shot("02-configuracoes");
  check("Testar conexão", true, test);

  // ---------- 3. upload pelo navegador + senha pedida no navegador + envio conferido ----------
  await page.click('.nav[data-page="queue"]');
  shotsTaken.vazia = await shot("03-fila-vazia");
  await page.setInputFiles("#fileInput", parts);
  await page.locator("#pwDialog[open]").waitFor({ timeout: 60000 });
  const pwTitle1 = await page.locator("#pwTitle").textContent();
  shotsTaken.senha = await shot("04-senha");
  await page.fill("#pwInput", "errada");
  await page.click('#pwDialog button[value="ok"]');
  await until(async () => (await page.locator("#pwTitle").textContent()) === "Senha incorreta" && await page.locator("#pwDialog").evaluate(d => d.open), 20000, "pedido de novo (senha incorreta)");
  await page.fill("#pwInput", "webpass");
  await page.click('#pwDialog button[value="ok"]');
  await until(async () => (await stage("Jogo Web")) === "Enviando" && await pct("Jogo Web") > 5, 60000, "Jogo Web enviando");
  shotsTaken.enviando = await shot("05-enviando");
  const cardDuring = await page.locator("#statusTitle").textContent();
  await until(async () => ["Verificado", "Erro"].includes(await stage("Jogo Web")), 120000, "Jogo Web terminar");
  const w1 = row("Jogo Web");
  const w1Stage = await w1.getAttribute("data-stage"), w1Detail = await w1.locator(".detail").textContent();
  const tid = await w1.locator(".tid").textContent();
  const coverOk = await w1.locator(".cover").evaluate(i => i.complete && i.naturalWidth === 64);
  const remoteGame = path.join(ftpRoot, "mnt", "ext1", "homebrew", game);
  const files = (function walk(d) { return fs.readdirSync(d, { withFileTypes: true }).flatMap(e => e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]); })(gameDir);
  const same = files.filter(f => { const r = path.join(remoteGame, path.relative(gameDir, f)); return fs.existsSync(r) && sha(r) === sha(f); }).length;
  const learned = (await getSettings()).knownPasswords.includes("webpass");
  shotsTaken.concluido = await shot("06-concluido");
  const filesSent = ftpLog.filter(l => (l.includes("STOR ") || l.includes("APPE ")) && l.includes(game)).length;
  check("Upload pelo navegador → senha no navegador → envio ao PS5", w1Stage === "Verificado" && same === files.length && pwTitle1 === "Arquivo protegido por senha" && learned && tid === "PPSA09001" && coverOk && cardDuring === "PS5 online",
    `${parts.length} volumes .7z enviados pela página; diálogo "${pwTitle1}", 1ª senha errada → "Senha incorreta", 2ª certa; estado ${w1Stage} (${w1Detail}); ${same}/${files.length} SHA-256 iguais; capa e ${tid}; senha entrou nas senhas conhecidas=${learned}; cartão durante o envio "${cardDuring}"`);

  // ---------- 4. pasta monitorada + pausar/retomar pela página + reiniciar o container no meio ----------
  fs.copyFileSync(imgw, path.join(games, "IMGW.exfat"));
  await until(async () => (await stage("IMGW")) === "Enviando" && await pct("IMGW") > 3, 60000, "IMGW enviando");
  await row("IMGW").locator('[data-act="pause"]').click();
  await until(async () => (await stage("IMGW")) === "Pausado", 5000, "IMGW pausado");
  await sleep(600);
  const p1 = await pct("IMGW"); await sleep(1500); const p2 = await pct("IMGW");
  shotsTaken.pausado = await shot("07-pausado");
  await row("IMGW").locator('[data-act="resume"]').click();
  await until(async () => (await stage("IMGW")) === "Enviando" && await pct("IMGW") > p2 + 5, 30000, "IMGW retomado");
  const before = await pct("IMGW");
  const cut = fs.statSync(path.join(ftpRoot, "mnt", "ext1", "homebrew", "IMGW.exfat.ferry-part")).size;
  docker("restart", "-t", "2", name);
  await until(up200, 30000, "container de volta");
  await until(async () => (await stage("IMGW")) === "Verificado", 180000, "IMGW concluído depois de reiniciar");
  const stillIn = await page.locator("#shell").isVisible() && !(await page.locator("#auth").isVisible());
  // o jogo já enviado (partes ainda na pasta monitorada) volta como Concluído, não como "já instalado"
  const w1After = await stage("W1"), w1AfterDetail = await row("W1").locator(".detail").textContent(); // sem reenviar, o card volta com o nome do arquivo
  const w1Sent = ftpLog.filter(l => (l.includes("STOR ") || l.includes("APPE ")) && l.includes(game)).length;
  const remoteImg = path.join(ftpRoot, "mnt", "ext1", "homebrew", "IMGW.exfat");
  const appe = ftpLog.filter(l => l.includes("APPE ") && l.includes("IMGW.exfat.ferry-part"));
  const imgOk = fs.existsSync(remoteImg) && sha(remoteImg) === sha(imgw) && !fs.existsSync(remoteImg + ".ferry-part");
  check("Pausar e retomar pela página", p1 === p2 && p1 > 0 && p1 < 100, `congelou em ${p1}% por 1,5 s e retomou`);
  check("Reiniciar o container no meio do envio", imgOk && appe.length > 0 && stillIn && w1After === "Verificado" && w1Sent === filesSent,
    `docker restart em ${before}% (${cut} bytes no PS5); voltou sozinho, continuou com APPE (${appe.length}x), hash confere, sem .ferry-part; login continuou valendo=${stillIn}; Jogo Web continuou ${w1After} ("${w1AfterDetail}") sem reenviar nada`);

  // ---------- 5. upload que continua de onde parou ----------
  const st = fs.statSync(up), last = Math.floor(st.mtimeMs);
  const start = await (await page.request.post(base + "/api/uploads", { data: { name: "UP.exfat", size: st.size, lastModified: last } })).json();
  const first = fs.readFileSync(up).subarray(0, 16 << 20);
  const r1 = await page.request.put(`${base}/api/uploads/${start.id}?offset=0`, { data: first, headers: { "Content-Type": "application/octet-stream" } });
  const wrong = await page.request.put(`${base}/api/uploads/${start.id}?offset=0`, { data: first, headers: { "Content-Type": "application/octet-stream" } });
  await sleep(6000); // mais que a janela de estabilidade: parcial não pode virar jogo
  const partialHidden = (await row("UP").count()) === 0;
  const offsets = [];
  page.on("request", r => { if (r.method() === "PUT" && r.url().includes("/api/uploads/")) offsets.push(Number(new URL(r.url()).searchParams.get("offset"))); });
  await page.setInputFiles("#fileInput", [up]);
  await until(async () => (await stage("UP")) === "Verificado", 120000, "UP concluído");
  const remoteUp = path.join(ftpRoot, "mnt", "ext1", "homebrew", "UP.exfat");
  const upOk = r1.ok() && wrong.status() === 409 && partialHidden && offsets[0] === first.length && fs.existsSync(remoteUp) && sha(remoteUp) === sha(up) && sha(path.join(games, "UP.exfat")) === sha(up);
  check("Upload continua de onde parou", upOk,
    `1º bloco (16 MB) pela API; bloco repetido → ${wrong.status()}; parcial não entrou na fila=${partialHidden}; a página continuou do byte ${offsets[0]} (${offsets.length} bloco(s)); arquivo no servidor e no PS5 com o mesmo hash`);

  // ---------- 6. sair e entrar ----------
  await page.click('.nav[data-page="settings"]');
  await page.click("#logout");
  await page.locator("#auth").waitFor();
  const after = (await page.request.get(base + "/api/settings")).status();
  await page.fill("#authUser", "admin");
  await page.fill("#authPass", "errada-demais");
  await page.click("#authBtn");
  const bad = await until(async () => (await page.locator("#authError").textContent()) || null, 5000, "erro de login");
  shotsTaken.entrar = await shot("08-entrar");
  await page.fill("#authPass", "senha-do-e2e");
  await page.click("#authBtn");
  await page.locator("#shell").waitFor();
  check("Sair e entrar de novo", after === 401 && bad === "Usuário ou senha incorretos.", `depois de sair a API responde ${after}; senha errada → "${bad}"; certa → entrou`);

  // ---------- 7. celular ----------
  const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, storageState: await ctx.storageState(), deviceScaleFactor: 2 });
  const pp = await phone.newPage();
  await pp.goto(base);
  await pp.locator(".job").first().waitFor();
  const scroll = await pp.evaluate(() => document.documentElement.scrollWidth);
  await pp.screenshot({ path: path.join(shots, "09-celular.png"), fullPage: true });
  shotsTaken.celular = "e2e/web/report/09-celular.png";
  check("Celular (390 px)", scroll <= 390, `largura da página ${scroll}px, sem rolagem lateral`);
  await phone.close();
  check("Sem erro de JavaScript na página", pageErrors.length === 0, pageErrors.join("; ") || "nenhum");
} catch (e) {
  failed = e;
  check("Execução", false, e.message);
  try { await shot("erro"); } catch { }
}

await browser.close();
const logs = (() => { try { return execFileSync("docker", ["logs", name], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }); } catch (e) { return String(e.stdout ?? e); } })();
fs.writeFileSync(path.join(work, "container.log"), logs);
fs.writeFileSync(path.join(work, "ftp.log"), ftpLog.join("\n"));
try { docker("rm", "-f", name); } catch { }
ftp.kill();

const ok = results.every(r => r.ok);
const sevenVer = execFileSync(sevenZip, [], { encoding: "utf8" }).split("\n").find(l => l.trim()) ?? "";
const md = [
  "# Relatório E2E web — Ferry self-hosted",
  "",
  `- Data: ${new Date().toISOString().replace("T", " ").slice(0, 19)} UTC`,
  `- Resultado geral: **${ok ? "PASSOU" : "FALHOU"}**  (${Math.round((Date.now() - t0) / 1000)}s)`,
  `- Imagem: \`${image}\` rodando com \`--network host\` e \`--user ${process.getuid()}:${process.getgid()}\`, volumes /data e /games; navegador Chromium (Playwright)`,
  `- PS5 falso: pyftpdlib imitando o ftpsrv novo (com APPE), 40 MB/s por conexão; arquivos de teste gerados com ${sevenVer.trim()}`,
  "",
  "| Verificação | Resultado |",
  "|---|---|",
  ...results.map(r => `| ${r.what} | ${r.ok ? "✅" : "❌"} ${r.detail ?? ""} |`),
  "",
  "## Capturas",
  "",
  ...Object.values(shotsTaken).map(p => `![${path.basename(p, ".png")}](${p})`),
  "",
  "Repetir: `docker build -t ferry:e2e . && cd e2e/web && npm ci && node web.mjs`. Requer Docker, Python com `pyftpdlib` e 7-Zip.",
  "",
].join("\n");
fs.writeFileSync(path.join(root, "e2e_report_web.md"), md);
console.log("\n" + md);
if (!ok) console.log("\n--- log do container ---\n" + logs.slice(-6000));
process.exit(ok ? 0 : 1);
