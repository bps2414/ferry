// Vitrine da interface web: sobe o servidor local, cria o acesso e injeta na Fila um estado fixo com todos os casos
// (erro, PKG pronto, enviando, pausado, aguardando partes, concluídos). Gera capturas comparáveis entre versões e mede a
// renderização da fila. Não testa comportamento — isso é o web.mjs.
//
// Uso: dotnet build web -c Release && (cd e2e/web && node showcase.mjs [pasta-de-saída])
//      FERRY_WEB_DLL=<outro ferry.dll> compara outra versão com o mesmo estado.
import { chromium } from "playwright";
import { spawn } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..", "..");
const out = path.resolve(process.argv[2] || path.join(root, "e2e", "web", "report", "showcase"));
const dll = process.env.FERRY_WEB_DLL || path.join(root, "web", "bin", "Release", "net10.0", "ferry.dll");
if (!fs.existsSync(dll)) throw new Error("Build web Release first: " + dll);
fs.mkdirSync(out, { recursive: true });
const data = fs.mkdtempSync(path.join(os.tmpdir(), "ferry-showcase-"));
const port = await new Promise(r => { const s = net.createServer().listen(0, "127.0.0.1", () => { const p = s.address().port; s.close(() => r(p)); }); });
const server = spawn("dotnet", [dll], { env: { ...process.env, FERRY_DATA: data, FERRY_GAMES: path.join(data, "games"), FERRY_PORT: String(port) }, stdio: "ignore" });
process.once("exit", () => server.kill());
const base = `http://127.0.0.1:${port}`;
for (let i = 0; ; i++) {
  try { if ((await fetch(base + "/")).ok) break; } catch { }
  if (i > 300) throw new Error("server did not start");
  await new Promise(r => setTimeout(r, 100));
}

const GB = 2 ** 30;
const job = (id, o) => ({
  id, name: o.name ?? o.title, title: o.title ?? "", titleId: o.titleId ?? "", packageFormat: o.packageFormat ?? null, stage: o.stage,
  stageText: "", stageMessage: null, detail: o.detail ?? "", detailMessage: null, currentFile: o.file ?? "",
  progress: o.progress ?? 0, amount: "", rateValue: "", rateUnit: "", etaValue: "", etaUnit: "",
  doneBytes: Math.round((o.total ?? 0) * (o.progress ?? 0) / 100), totalBytes: o.total ?? 0, rate: o.rate ?? 0, secondsRemaining: o.eta ?? null,
  icon: o.icon ? 1 : 0, isActive: !!o.active, canPause: !!o.active, canResume: o.stage === "Pausado", canCancel: !["Verificado", "InstalacaoSolicitada", "Erro"].includes(o.stage),
  canRetry: o.stage === "Erro", canSendNow: o.stage === "Pausado" || o.stage === "NaFila", canRequestInstall: o.stage === "PacotePronto",
});
const texts = {
  "pt-BR": { parts: "faltando part3.rar", wrong: "Senha incorreta. Use Tentar de novo para digitar outra.", pkg: "PKG enviado para /data/etaHEN/pkgs. Solicite a instalação ou abra o Package Installer no PS5.", done: "Enviado e conferido antes de reabrir", req: "Solicitação aceita. Acompanhe a instalação no PS5." },
  en: { parts: "missing part3.rar", wrong: "Incorrect password. Use Retry to enter another one.", pkg: "PKG sent to /data/etaHEN/pkgs. Request installation or open Package Installer on the PS5.", done: "Sent and verified before reopening", req: "Request accepted. Follow the installation on the PS5." },
};
function state(locale, extra = {}) {
  const x = texts[locale];
  const jobs = [
    job("kestrel", { name: "Kestrel_Run_v1.02", stage: "AguardandoPartes", detail: x.parts }),
    job("hollow", { title: "Hollow Meridian", titleId: "PPSA04512", stage: "Enviando", active: true, progress: 62.4, total: 29.2 * GB, rate: 87.3 * 2 ** 20, eta: 129, icon: "sun", file: "Image0/data/level_07.pak" }),
    job("ashen", { title: "Ashen Tide", titleId: "PPSA07731", stage: "Pausado", progress: 18, total: 41.87 * GB, icon: "tide" }),
    job("blue", { name: "Blue_Harbor_v1.00.rar", stage: "Erro", detail: x.wrong }),
    job("pkgweb", { name: "PKGWEB", packageFormat: "PS4 / CNT", stage: "PacotePronto", progress: 100, total: 3.1 * GB, detail: x.pkg }),
    job("north", { title: "Northbound", titleId: "PPSA01180", stage: "Verificado", progress: 100, total: 12.4 * GB, icon: "north", detail: x.done }),
    job("imgw", { name: "IMGW", stage: "Verificado", progress: 100, total: 0.4 * GB, detail: x.done }),
    job("pkgarch", { name: "PKGARCH", packageFormat: "PS4 / CNT", stage: "InstalacaoSolicitada", progress: 100, total: 2.2 * GB, detail: x.req }),
    job("w1", { title: "Jogo Web", titleId: "PPSA09001", stage: "Verificado", progress: 100, total: 0.12 * GB, icon: "sun", detail: x.done }),
    ...(extra.jobs || []),
  ];
  const count = (...s) => jobs.filter(j => s.includes(j.stage)).length;
  return {
    language: locale, jobs, password: extra.password ?? null,
    summary: { sending: count("Extraindo", "Enviando"), queued: count("NaFila", "AguardandoPartes", "Pausado"), done: count("Verificado"), requested: count("InstalacaoSolicitada"),
      ready: count("PacotePronto"), submitting: 0, unknown: 0, errors: count("Erro"), rate: jobs.filter(j => j.isActive).reduce((a, j) => a + j.rate, 0) },
    ps5: { host: "192.168.1.50", port: 2121, remoteDir: "/mnt/ext1/homebrew", status: "online", message: "", messageData: null, found: null },
  };
}
const covers = {
  sun: `<rect width="64" height="64" fill="#13263b"/><circle cx="40" cy="30" r="12" fill="#e9a54f"/><path d="M0 34h64" stroke="#e9a54f"/><path d="M6 34l8-10 6 6 5-8 8 12z" fill="#0c1826"/>`,
  tide: `<rect width="64" height="64" fill="#26262a"/><rect x="14" y="10" width="3" height="26" fill="#cbbba5"/><rect x="20" y="16" width="3" height="20" fill="#8d806f"/><path d="M0 44q16-8 32 0t32 0v20H0z" fill="#3a3a40"/>`,
  north: `<rect width="64" height="64" fill="#1b2b33"/><path d="M0 50 22 18l12 16 8-10 22 26z" fill="#d7e3ea"/><path d="M0 50 22 18l6 8-10 24z" fill="#9fb3bf"/>`,
};

const browser = await chromium.launch();
const results = {};
async function session(locale, viewport, events) {
  const ctx = await browser.newContext({ viewport, locale, deviceScaleFactor: 1 });
  const page = await ctx.newPage();
  await page.route("**/api/jobs/*/icon*", route => {
    const id = route.request().url().split("/api/jobs/")[1].split("/")[0];
    const art = { hollow: covers.sun, ashen: covers.tide, north: covers.north, w1: covers.sun }[id] || covers.north;
    route.fulfill({ contentType: "image/svg+xml", body: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">${art}</svg>` });
  });
  await page.route("**/api/events", route => route.fulfill({ status: 200, contentType: "text/event-stream", headers: { "Cache-Control": "no-cache" }, body: events() }));
  await ctx.addCookies([{ name: cookieName, value: cookieValue, url: base }]);
  return { ctx, page };
}
const sse = (name, payload) => `event: ${name}\ndata: ${JSON.stringify(payload)}\n\nretry: 600000\n\n`;

// acesso e configurações reais (só a fila é injetada)
const setup = await fetch(base + "/api/auth/setup", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ user: "admin", password: "showcase-pass" }) });
const cookie = setup.headers.get("set-cookie").split(";")[0];
await fetch(base + "/api/settings", { method: "PUT", headers: { "Content-Type": "application/json", cookie }, body: JSON.stringify({ host: "192.168.1.50", port: 2121 }) });
const [cookieName, cookieValue] = cookie.split("=");

for (const locale of ["pt-BR", "en"]) {
  await fetch(base + "/api/settings", { method: "PUT", headers: { "Content-Type": "application/json", cookie }, body: JSON.stringify({ language: locale }) });
  for (const [label, viewport] of [["desktop", { width: 1440, height: 900 }], ["celular", { width: 390, height: 844 }]]) {
    const { ctx, page } = await session(locale, viewport, () => sse("state", state(locale)));
    await page.goto(base + "/");
    await page.locator(".job").first().waitFor({ state: "attached" });
    await page.waitForTimeout(3000); // a marca termina a animação de entrada
    await page.screenshot({ path: path.join(out, `${locale}-fila-${label}.png`) });
    if (label === "celular") await page.screenshot({ path: path.join(out, `${locale}-fila-${label}-inteira.png`), fullPage: true });
    // a fila inteira: quanto rola para ver tudo (concluídos recolhidos contam como uma linha)
    results[`${locale} ${label} altura da fila (px)`] = await page.evaluate(() => document.documentElement.scrollHeight);
    const toggle = page.locator("#doneToggle");
    if (await toggle.count()) { await toggle.click(); await page.waitForTimeout(250); await page.screenshot({ path: path.join(out, `${locale}-fila-${label}-concluidos-abertos.png`), fullPage: label === "celular" }); }
    await page.click('.nav[data-page="settings"]');
    await page.evaluate(() => scrollTo(0, 0));
    await page.waitForTimeout(300);
    await page.screenshot({ path: path.join(out, `${locale}-configuracoes-${label}.png`) });
    results[`${locale} ${label} altura das configurações (px)`] = await page.evaluate(() => document.documentElement.scrollHeight);
    await ctx.close();
  }
}

// avisos repetidos: senha pedida duas vezes para o mesmo jogo (a segunda depois de uma senha incorreta)
{
  const locale = "pt-BR";
  const notice = seq => sse("notice", { seq, kind: "password", titleMessage: { key: "web.passwordTitle", args: [] }, textMessage: { key: "web.passwordText", args: ["Blue_Harbor_v1.00.rar"] }, title: "", text: "", jobId: "blue" });
  const { ctx, page } = await session(locale, { width: 1440, height: 900 }, () => sse("state", state(locale)) + notice(1) + notice(2));
  await page.goto(base + "/");
  await page.locator(".toast").first().waitFor();
  await page.waitForTimeout(300);
  results["avisos na tela para 2 pedidos de senha do mesmo jogo"] = await page.locator(".toast").count();
  await page.screenshot({ path: path.join(out, `${locale}-avisos-repetidos.png`) });
  await ctx.close();
}

// desempenho: render() com 200 jogos, 100 atualizações de progresso (o SSE manda até 4/s)
{
  const locale = "pt-BR";
  const big = state(locale, { jobs: Array.from({ length: 191 }, (_, i) => job("bulk" + i, { name: `Jogo_${String(i).padStart(3, "0")}.rar`, stage: i % 3 ? "NaFila" : "Verificado", progress: i % 3 ? 0 : 100, total: (i + 1) * 0.1 * GB })) });
  const { ctx, page } = await session(locale, { width: 1440, height: 900 }, () => sse("state", state(locale)));
  await page.goto(base + "/");
  await page.locator(".job").first().waitFor({ state: "attached" });
  const perf = await page.evaluate(big => {
    render(big);
    const times = [];
    for (let i = 0; i < 100; i++) {
      big.jobs[1].progress = 62.4 + i / 10; big.jobs[1].doneBytes += 2 ** 20 * 20;
      const t0 = performance.now(); render(big); document.body.offsetHeight; times.push(performance.now() - t0);
    }
    times.sort((a, b) => a - b);
    return { median: times[50], p95: times[95], nodes: document.querySelectorAll("*").length, rows: document.querySelectorAll(".job").length };
  }, big);
  results["render 200 jogos — mediana (ms)"] = +perf.median.toFixed(2);
  results["render 200 jogos — p95 (ms)"] = +perf.p95.toFixed(2);
  results["elementos no DOM com 200 jogos"] = perf.nodes;
  await ctx.close();
}

await browser.close();
server.kill();
fs.writeFileSync(path.join(out, "medidas.json"), JSON.stringify(results, null, 2) + "\n");
console.log(JSON.stringify(results, null, 2));
fs.rmSync(data, { recursive: true, force: true });
