import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

const options = Object.fromEntries(
  process.argv.slice(2).map(argument => argument.replace(/^--/, "").split("="))
);

const mainRun = options.main;
const facetRun = options.facets;
const output = options.out ?? mainRun;

const providers = [
  { key: "Examine", label: "Examine", color: "#2a78d6" },
  { key: "Algolia", label: "Algolia", color: "#eb6834" },
  { key: "Azure", label: "Azure AI Search", color: "#1baf7a" },
];

const ink = { primary: "#0b0b0b", secondary: "#52514e", muted: "#898781", grid: "#e1e0d9", baseline: "#c3c2b7", surface: "#fcfcfb" };

const load = async folder => JSON.parse(await readFile(join(folder, "results.json"), "utf8"));

const escape = text => String(text).replace(/&/g, "&amp;").replace(/</g, "&lt;");

function page(title, subtitle, svg, footnote) {
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>${escape(title)}</title>
<style>
  html, body { margin: 0; background: ${ink.surface}; font-family: system-ui, -apple-system, "Segoe UI", sans-serif; color: ${ink.primary}; }
  .frame { width: 1920px; height: 1080px; box-sizing: border-box; padding: 72px 96px 56px; display: flex; flex-direction: column; }
  h1 { margin: 0; font-size: 52px; font-weight: 700; letter-spacing: -0.01em; }
  .subtitle { margin: 12px 0 0; font-size: 26px; color: ${ink.secondary}; }
  .legend { display: flex; gap: 40px; margin: 28px 0 8px; font-size: 24px; color: ${ink.secondary}; }
  .legend span { display: inline-flex; align-items: center; gap: 12px; }
  .legend i { width: 22px; height: 22px; border-radius: 4px; display: inline-block; }
  svg { flex: 1; width: 100%; }
  .foot { font-size: 20px; color: ${ink.muted}; margin-top: 8px; }
  .bar-group rect:hover { opacity: .85; }
</style></head>
<body><div class="frame">
<h1>${escape(title)}</h1>
<p class="subtitle">${escape(subtitle)}</p>
<div class="legend">${providers.map(p => `<span><i style="background:${p.color}"></i>${escape(p.label)}</span>`).join("")}</div>
${svg}
<div class="foot">${escape(footnote)}</div>
</div></body></html>`;
}

function roundedBar(x, y, width, height, color, title) {
  const r = Math.min(4, width / 2, height / 2);
  const w = Math.max(width, 2);
  const path = `M${x},${y} H${x + w - r} Q${x + w},${y} ${x + w},${y + r} V${y + height - r} Q${x + w},${y + height} ${x + w - r},${y + height} H${x} Z`;
  return `<path d="${path}" fill="${color}"><title>${escape(title)}</title></path>`;
}

function groupedBars(groups, maxValue, { width = 1728, height = 700, labelWidth = 520 } = {}) {
  const plotWidth = width - labelWidth - 140;
  const groupGap = 26;
  const barGap = 2;
  const barsPerGroup = groups[0].bars.length;
  const groupHeight = (height - groupGap * (groups.length - 1)) / groups.length;
  const barHeight = Math.min(34, (groupHeight - barGap * (barsPerGroup - 1)) / barsPerGroup);
  const scale = value => (value / maxValue) * plotWidth;
  const ticks = niceTicks(maxValue);
  let svg = `<svg viewBox="0 0 ${width} ${height + 50}" xmlns="http://www.w3.org/2000/svg" font-family="system-ui, -apple-system, 'Segoe UI', sans-serif">`;
  for (const tick of ticks) {
    const x = labelWidth + scale(tick);
    svg += `<line x1="${x}" x2="${x}" y1="0" y2="${height}" stroke="${tick === 0 ? ink.baseline : ink.grid}" stroke-width="${tick === 0 ? 2 : 1}"/>`;
    svg += `<text x="${x}" y="${height + 36}" font-size="20" fill="${ink.muted}" text-anchor="middle" style="font-variant-numeric: tabular-nums">${tick} ms</text>`;
  }
  groups.forEach((group, groupIndex) => {
    const top = groupIndex * (groupHeight + groupGap);
    const blockHeight = barsPerGroup * barHeight + (barsPerGroup - 1) * barGap;
    const blockTop = top + (groupHeight - blockHeight) / 2;
    svg += `<text x="${labelWidth - 28}" y="${blockTop + blockHeight / 2 + 9}" font-size="26" fill="${ink.primary}" text-anchor="end" font-weight="600">${escape(group.label)}</text>`;
    if (group.sublabel) {
      svg += `<text x="${labelWidth - 28}" y="${blockTop + blockHeight / 2 + 40}" font-size="20" fill="${ink.secondary}" text-anchor="end">${escape(group.sublabel)}</text>`;
    }
    svg += `<g class="bar-group">`;
    group.bars.forEach((bar, barIndex) => {
      const y = blockTop + barIndex * (barHeight + barGap);
      const w = scale(bar.value);
      svg += roundedBar(labelWidth, y, w, barHeight, bar.color, `${group.label} · ${bar.name}: ${bar.value.toFixed(1)} ms`);
      svg += `<text x="${labelWidth + Math.max(w, 2) + 12}" y="${y + barHeight / 2 + 8}" font-size="22" fill="${ink.secondary}" style="font-variant-numeric: tabular-nums">${bar.value < 10 ? bar.value.toFixed(1) : Math.round(bar.value)} ms</text>`;
    });
    svg += `</g>`;
  });
  return svg + "</svg>";
}

function niceTicks(max) {
  const step = max > 60 ? 20 : max > 30 ? 10 : 5;
  const ticks = [];
  for (let value = 0; value <= max + 0.0001; value += step) ticks.push(value);
  return ticks;
}

const main = await load(mainRun);
const facets = facetRun ? await load(facetRun) : null;

const mainGroups = main.summary.map(row => ({
  label: row.label,
  bars: providers.map(p => ({ name: p.label, color: p.color, value: row.providers[p.key].providerTime.median })),
}));
const mainMax = Math.ceil(Math.max(...mainGroups.flatMap(g => g.bars.map(b => b.value))) / 10) * 10 + 10;
const runDate = new Date(main.metadata.runAt).toLocaleDateString("en-GB", { day: "numeric", month: "long", year: "numeric" });

await writeFile(join(output, "chart-1-median-by-search.html"), page(
  "Median search time by type of search",
  `Median of ${main.metadata.iterations} runs after ${main.metadata.warmup} warm-up runs · 5,035 documents · every search requests all 12 facets`,
  groupedBars(mainGroups, mainMax),
  `Measured ${runDate} from a developer laptop. Algolia Free plan and Azure AI Search Free tier; cloud times include the network round trip. Lower is faster.`
));

if (facets) {
  const pick = (key, provider) => facets.summary.find(row => row.scenario === key).providers[provider].providerTime.median;
  const facetGroups = providers.map(p => ({
    label: p.label,
    bars: [
      { name: "No facets", color: p.color, value: pick("keyword-no-facets", p.key) },
      { name: "12 facets", color: p.color, value: pick("keyword", p.key) },
    ],
  }));
  const facetMax = Math.ceil(Math.max(...facetGroups.flatMap(g => g.bars.map(b => b.value))) / 10) * 10 + 10;

  const rows = facetGroups.flatMap(group => group.bars.map(bar => ({ label: `${group.label}`, sublabel: bar.name, bars: [bar] })));
  await writeFile(join(output, "chart-2-facet-cost.html"), page(
    "What facets cost each provider",
    `Keyword search "chair", median of ${facets.metadata.iterations} runs · same moment, same 5,035 documents`,
    groupedBars(rows, facetMax, { height: 760 }),
    `Without facets Examine answers in about 1 millisecond. The cloud providers stay near 45–55 ms either way, which suggests most of their time is the network round trip (not measured separately). Measured ${runDate}.`
  ));
}

console.log(`Charts written to ${output}`);
