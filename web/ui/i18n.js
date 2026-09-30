"use strict";
const catalogs = {};
let locale = "en", languageMode = "auto";
let fmt1 = new Intl.NumberFormat(locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 });

async function loadTranslations() {
  await Promise.all(["pt-BR", "en"].map(async name => {
    const response = await fetch(`/i18n/${name}.json`);
    if (!response.ok) throw new Error(`Translation catalog: ${response.status}`);
    catalogs[name] = await response.json();
  }));
}
function resolvedLocale(mode) {
  if (mode === "pt-BR" || mode === "en") return mode;
  for (const preference of navigator.languages || [navigator.language]) {
    if (/^pt(?:-|$)/i.test(preference)) return "pt-BR";
    if (/^en(?:-|$)/i.test(preference)) return "en";
  }
  return "en";
}
function renderMessage(message) {
  if (message == null) return "";
  if (typeof message !== "object" || !message.key) return String(message);
  if (message.key === "core.size") return size(message.args[0]).join(" ");
  return t(message.key, ...(message.args || []));
}
function t(key, ...args) {
  if (typeof args[0] === "number" && catalogs[locale]?.[key + ".one"] != null) key += args[0] === 1 ? ".one" : ".other";
  const template = catalogs[locale]?.[key] ?? catalogs.en?.[key] ?? key;
  return template.replace(/\{(\d+)\}/g, (token, index) => index < args.length ? renderMessage(args[index]) : token);
}
function message(key, ...args) { return { key, args }; }
function translateElements(root = document) {
  for (const el of root.querySelectorAll("[data-i18n]")) el.textContent = t(el.dataset.i18n);
  for (const attr of ["title", "aria-label"]) {
    for (const el of root.querySelectorAll(`[data-i18n-${attr}]`)) el.setAttribute(attr, t(el.getAttribute(`data-i18n-${attr}`)));
  }
}
function setLanguage(mode = "auto") {
  languageMode = mode;
  locale = resolvedLocale(mode);
  fmt1 = new Intl.NumberFormat(locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  document.documentElement.lang = locale;
  translateElements();
  translateElements(document.querySelector("#jobTpl").content);
  rerenderTranslations();
}
