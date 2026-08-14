#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";

const args = new Map();
for (const item of process.argv.slice(2)) {
  const [key, value = "true"] = item.split("=", 2);
  args.set(key, value);
}

const dataDirectory = args.get("--data-dir") ?? "/private/tmp";
const fixtureDates = (args.get("--fixture-dates") ?? "2026-08-08,2026-08-09,2026-08-10")
  .split(",")
  .map(value => value.trim())
  .filter(Boolean);
const utcOffset = args.get("--utc-offset") ?? "-04:00";

const readJson = filePath => JSON.parse(fs.readFileSync(filePath, "utf8"));
const batchFiles = [1, 2, 3, 4]
  .map(number => path.join(dataDirectory, `botc-backtest-${number}.json`))
  .filter(fs.existsSync);
if (batchFiles.length === 0) {
  throw new Error(`No botc-backtest-*.json files were found in ${dataDirectory}.`);
}

const batches = batchFiles.map(readJson);
const selections = batches.flatMap(batch => batch.Selections.map(row => row.Selection));
const fixtures = fixtureDates.flatMap(date => {
  const filePath = path.join(dataDirectory, `api-football-fixtures-${date}.json`);
  return fs.existsSync(filePath) ? readJson(filePath).response ?? [] : [];
});

function normalize(value) {
  return (value ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/\b(fc|cf|ca|sc|afc|fk|sk|cd|club|de|the|women|w)\b/g, " ")
    .replace(/[^a-z0-9]+/g, " ")
    .trim();
}

function levenshtein(left, right) {
  const row = Array.from({ length: right.length + 1 }, (_, index) => index);
  for (let leftIndex = 1; leftIndex <= left.length; leftIndex += 1) {
    let diagonal = row[0];
    row[0] = leftIndex;
    for (let rightIndex = 1; rightIndex <= right.length; rightIndex += 1) {
      const previous = row[rightIndex];
      row[rightIndex] = Math.min(
        row[rightIndex] + 1,
        row[rightIndex - 1] + 1,
        diagonal + (left[leftIndex - 1] === right[rightIndex - 1] ? 0 : 1));
      diagonal = previous;
    }
  }
  return row[right.length];
}

function similarity(left, right) {
  const normalizedLeft = normalize(left);
  const normalizedRight = normalize(right);
  if (!normalizedLeft || !normalizedRight) return 0;
  if (normalizedLeft === normalizedRight) return 1;
  if (normalizedLeft.includes(normalizedRight) || normalizedRight.includes(normalizedLeft)) {
    return 0.8 + 0.2 * Math.min(normalizedLeft.length, normalizedRight.length) /
      Math.max(normalizedLeft.length, normalizedRight.length);
  }
  return 1 - levenshtein(normalizedLeft, normalizedRight) /
    Math.max(normalizedLeft.length, normalizedRight.length);
}

function matchFixture(selection) {
  const kickoff = new Date(`${selection.MatchDate}${utcOffset}`).getTime();
  const candidates = fixtures
    .map(fixture => {
      const hoursDifference = Math.abs(new Date(fixture.fixture.date).getTime() - kickoff) / 3_600_000;
      const homeSimilarity = similarity(selection.HomeTeam, fixture.teams.home.name);
      const awaySimilarity = similarity(selection.AwayTeam, fixture.teams.away.name);
      const teamSimilarity = (homeSimilarity + awaySimilarity) / 2;
      return {
        fixture,
        hoursDifference,
        teamSimilarity,
        score: teamSimilarity - hoursDifference * 0.04
      };
    })
    .filter(candidate => candidate.hoursDifference <= 4)
    .sort((left, right) => right.score - left.score);
  const best = candidates[0];
  if (!best || best.teamSimilarity < 0.72 || best.hoursDifference > 1) {
    return null;
  }
  return best;
}

function readCornerStats(fixture) {
  const filePath = path.join(dataDirectory, `api-football-stats-${fixture.fixture.id}.json`);
  if (!fs.existsSync(filePath)) return null;
  const response = readJson(filePath).response ?? [];
  if (response.length < 2) return null;

  const cornerValue = row => {
    const value = row.statistics?.find(item => item.type === "Corner Kicks")?.value;
    const number = Number(value);
    return Number.isFinite(number) ? number : null;
  };
  const byHomeSimilarity = [...response].sort(
    (left, right) => similarity(fixture.teams.home.name, right.team?.name) -
      similarity(fixture.teams.home.name, left.team?.name));
  const homeRow = byHomeSimilarity[0];
  const awayRow = response.find(row => row !== homeRow);
  const home = cornerValue(homeRow);
  const away = cornerValue(awayRow);
  return home === null || away === null ? null : { home, away, total: home + away };
}

function actualFor(selection, fixture) {
  if (selection.MarketType.includes("Goals")) {
    const home = Number(fixture.goals.home);
    const away = Number(fixture.goals.away);
    if (!Number.isFinite(home) || !Number.isFinite(away)) return null;
    if (selection.MarketType === "HomeTeamGoals") return home;
    if (selection.MarketType === "AwayTeamGoals") return away;
    return home + away;
  }

  const corners = readCornerStats(fixture);
  if (!corners) return null;
  if (selection.MarketType === "HomeTeamCorners") return corners.home;
  if (selection.MarketType === "AwayTeamCorners") return corners.away;
  return corners.total;
}

function asianComponents(line) {
  const floor = Math.floor(line);
  const fraction = Math.round((line - floor) * 100) / 100;
  if (fraction === 0.25) return [floor, floor + 0.5];
  if (fraction === 0.75) return [floor + 0.5, floor + 1];
  return [line];
}

function settle(actual, line, side, odds) {
  const outcomes = asianComponents(line).map(component => {
    if (actual === component) return "P";
    const won = side.toLowerCase() === "over" ? actual > component : actual < component;
    return won ? "W" : "L";
  });
  const profit = outcomes.reduce((total, outcome) => {
    if (outcome === "W") return total + (odds - 1) / outcomes.length;
    if (outcome === "L") return total - 1 / outcomes.length;
    return total;
  }, 0);
  const wins = outcomes.filter(outcome => outcome === "W").length;
  const losses = outcomes.filter(outcome => outcome === "L").length;
  const status = wins === outcomes.length ? "Won"
    : losses === outcomes.length ? "Lost"
      : wins > 0 ? "HalfWon"
        : losses > 0 ? "HalfLost"
          : "Push";
  return { status, profit };
}

const rows = selections.map(selection => {
  const common = {
    date: selection.MatchDate.slice(0, 10),
    league: selection.League,
    homeTeam: selection.HomeTeam,
    awayTeam: selection.AwayTeam,
    marketType: selection.MarketType,
    side: selection.SelectedSide,
    line: Number(selection.LineValue),
    odds: Number(selection.Odds),
    probabilityEdge: Number(selection.ProbabilityEdge),
    expectedValue: Number(selection.ExpectedValue),
    confidenceLevel: selection.ConfidenceLevel,
    modelConsensus: selection.ModelConsensus
  };
  const matched = matchFixture(selection);
  if (!matched) {
    return { ...common, fixtureId: null, actual: null, status: "Unmatched", profit: null };
  }
  const actual = actualFor(selection, matched.fixture);
  if (actual === null) {
    return {
      ...common,
      fixtureId: matched.fixture.fixture.id,
      actual: null,
      status: "NoStats",
      profit: null
    };
  }
  const result = settle(actual, Number(selection.LineValue), selection.SelectedSide, Number(selection.Odds));
  return {
    ...common,
    fixtureId: matched.fixture.fixture.id,
    actual,
    status: result.status,
    profit: result.profit
  };
});

function summarize(items) {
  const settled = items.filter(row => Number.isFinite(row.profit));
  const profit = settled.reduce((total, row) => total + row.profit, 0);
  const statusCounts = Object.fromEntries(
    ["Won", "HalfWon", "Push", "HalfLost", "Lost", "NoStats", "Unmatched"]
      .map(status => [status, items.filter(row => row.status === status).length]));
  return {
    picks: items.length,
    settled: settled.length,
    ...statusCounts,
    profit: Number(profit.toFixed(3)),
    roiPct: settled.length === 0 ? null : Number((profit / settled.length * 100).toFixed(2)),
    averageOdds: settled.length === 0 ? null : Number((settled.reduce((sum, row) => sum + row.odds, 0) / settled.length).toFixed(3))
  };
}

const groupBy = (items, keySelector) => Object.fromEntries(
  [...Map.groupBy(items, keySelector)].map(([key, values]) => [key, summarize(values)]));

const output = {
  scope: {
    oddsRows: batches[0]?.AvailableOddsRows ?? null,
    evaluatedGroups: batches.reduce((sum, batch) => sum + batch.TotalMatches, 0),
    selectedGroups: selections.length,
    skippedGroups: batches.reduce((sum, batch) => sum + batch.SkippedMatches, 0),
    errorGroups: batches.reduce((sum, batch) => sum + batch.ErrorMatches, 0),
    uniqueFixtures: new Set(rows.map(row => row.fixtureId).filter(Boolean)).size
  },
  overall: summarize(rows),
  byFamily: groupBy(rows, row => row.marketType?.includes("Goals") ? "Goals" : "Corners"),
  byDate: groupBy(rows, row => row.date ?? "Unknown"),
  byMarketType: groupBy(rows, row => row.marketType ?? "Unknown"),
  bySide: groupBy(rows, row => row.side ?? "Unknown"),
  byFamilyAndSide: groupBy(rows, row =>
    `${row.marketType?.includes("Goals") ? "Goals" : "Corners"}-${row.side ?? "Unknown"}`),
  byEdgeBand: groupBy(rows, row => row.probabilityEdge < 0.06 ? "<6%"
    : row.probabilityEdge < 0.10 ? "6-10%"
      : row.probabilityEdge < 0.15 ? "10-15%"
        : ">=15%"),
  byOddsBand: groupBy(rows, row => row.odds < 1.5 ? "<1.50"
    : row.odds < 1.7 ? "1.50-1.69"
      : row.odds < 2 ? "1.70-1.99"
        : ">=2.00"),
  byConfidence: groupBy(rows, row => row.confidenceLevel ?? "Unknown"),
  rows
};

console.log(JSON.stringify(output, null, 2));
