import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import os from "node:os";

const options = Object.fromEntries(
  process.argv.slice(2).map(argument => argument.replace(/^--/, "").split("="))
);

const baseUrl = options.url ?? "http://localhost:22518";
const warmup = Number(options.warmup ?? 5);
const iterations = Number(options.iterations ?? 50);
const outputRoot = options.out ?? "private/benchmark";
const providers = (options.providers ?? "Examine,Algolia,Azure").split(",");

const allScenarios = [
  { key: "keyword", label: "Keyword search", query: "q=chair" },
  { key: "keyword-facet", label: "Keyword + one facet ticked", query: "q=chair&category=Technology" },
  { key: "price-range", label: "Price range filter", query: "type=product&price=50-200" },
  { key: "sort-price", label: "Sort by price", query: "type=product&sort=price-asc" },
  { key: "typo", label: "Typo search", query: "q=chiar" },
  { key: "browse-all", label: "Browse everything (all facets)", query: "" },
  { key: "keyword-no-facets", label: "Keyword search, no facets", query: "q=chair&facets=none" },
  { key: "browse-no-facets", label: "Browse everything, no facets", query: "facets=none" },
];

const defaultScenarios = "keyword,keyword-facet,price-range,sort-price,typo,browse-all";
const scenarios = (options.scenarios ?? defaultScenarios).split(",").map(key => allScenarios.find(scenario => scenario.key === key));

const percentile = (sorted, p) => sorted[Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1))];

const summarise = values => {
  const sorted = [...values].sort((a, b) => a - b);
  const mean = sorted.reduce((sum, value) => sum + value, 0) / sorted.length;
  return {
    count: sorted.length,
    min: sorted[0],
    median: percentile(sorted, 50),
    mean,
    p95: percentile(sorted, 95),
    max: sorted[sorted.length - 1],
  };
};

async function search(provider, scenario) {
  const url = `${baseUrl}/api/showdown/search?provider=${provider}${scenario.query ? `&${scenario.query}` : ""}`;
  const started = performance.now();
  const response = await fetch(url);
  const body = await response.json();
  const roundTrip = performance.now() - started;
  if (!response.ok || body.error) {
    throw new Error(`${provider} / ${scenario.key}: ${body.error ?? response.status}`);
  }
  return { providerMs: body.providerMilliseconds, roundTripMs: roundTrip, total: body.total };
}

const indexes = await (await fetch(`${baseUrl}/api/showdown/indexes`)).json();
const algoliaBefore = indexes.algoliaSearchesThisSession;

console.log(`Warm-up: ${warmup} runs per scenario and provider`);
for (let round = 0; round < warmup; round++) {
  for (const scenario of scenarios) {
    for (const provider of providers) {
      await search(provider, scenario);
    }
  }
}

const raw = [];
console.log(`Measuring: ${iterations} runs per scenario and provider`);
for (let round = 0; round < iterations; round++) {
  for (const scenario of scenarios) {
    const order = providers.map((_, index) => providers[(index + round) % providers.length]);
    for (const provider of order) {
      const result = await search(provider, scenario);
      raw.push({ round, scenario: scenario.key, provider, ...result });
    }
  }
  if ((round + 1) % 10 === 0) {
    console.log(`  ${round + 1}/${iterations} rounds done`);
  }
}

const summary = scenarios.map(scenario => ({
  scenario: scenario.key,
  label: scenario.label,
  query: scenario.query,
  providers: Object.fromEntries(providers.map(provider => {
    const rows = raw.filter(row => row.scenario === scenario.key && row.provider === provider);
    return [provider, {
      results: rows[0]?.total,
      providerTime: summarise(rows.map(row => row.providerMs)),
      roundTrip: summarise(rows.map(row => row.roundTripMs)),
    }];
  })),
}));

const indexesAfter = await (await fetch(`${baseUrl}/api/showdown/indexes`)).json();
const stamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
const folder = join(outputRoot, stamp);
await mkdir(folder, { recursive: true });

const metadata = {
  runAt: new Date().toISOString(),
  machine: { platform: `${os.type()} ${os.release()}`, cpu: os.cpus()[0]?.model, cores: os.cpus().length, memoryGb: Math.round(os.totalmem() / 1024 ** 3) },
  node: process.version,
  baseUrl,
  warmup,
  iterations,
  providers,
  indexes: indexesAfter.indexes,
  algoliaSearchesUsed: indexesAfter.algoliaSearchesThisSession - algoliaBefore,
};

await writeFile(join(folder, "results.json"), JSON.stringify({ metadata, summary, raw }, null, 2));
await writeFile(
  join(folder, "raw.csv"),
  ["round,scenario,provider,providerMs,roundTripMs,total", ...raw.map(row => `${row.round},${row.scenario},${row.provider},${row.providerMs.toFixed(3)},${row.roundTripMs.toFixed(3)},${row.total}`)].join("\n")
);

const format = value => value.toFixed(1);
const table = [
  "| Search | Provider | Results | Median (ms) | 95th percentile (ms) | Fastest (ms) | Slowest (ms) |",
  "|---|---|---|---|---|---|---|",
  ...summary.flatMap(row => providers.map(provider => {
    const stats = row.providers[provider];
    return `| ${row.label} | ${provider} | ${stats.results} | ${format(stats.providerTime.median)} | ${format(stats.providerTime.p95)} | ${format(stats.providerTime.min)} | ${format(stats.providerTime.max)} |`;
  })),
].join("\n");

await writeFile(join(folder, "summary.md"), `# Benchmark ${metadata.runAt}\n\n${iterations} measured runs per search and provider, after ${warmup} warm-up runs. Times are provider time: from the moment the site asks the provider until the answer arrives, including the network round trip for cloud providers.\n\n${table}\n\nAlgolia searches used: ${metadata.algoliaSearchesUsed}\n`);

console.log(`\n${table}\n\nAlgolia searches used: ${metadata.algoliaSearchesUsed}\nSaved to ${folder}`);
