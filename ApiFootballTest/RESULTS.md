# API-Football v3 probe results

Run date: 2026-07-14

No rows were inserted into `MatchHistory`.

## Free-plan constraints observed

- The API reported a daily allowance of 100 requests and a rate limit of 10 requests per minute.
- `/leagues?country=Chile` exposed season metadata through 2026.
- `/leagues?country=Chile&season=2026` returned a plan error stating that the Free plan can access seasons 2022 through 2024.
- The `last` fixture filter was also rejected by the Free plan. Date ranges worked.

## Copa Chile 2024

- League ID: `267`
- Teams returned: `75`
- Coverage advertised events and lineups, but not fixture or player statistics.
- Finished fixtures reviewed: `10`
- Fixtures with fixture-statistic rows: `0`
- Fixtures with lineup rows: `10`
- Fixtures with both formations: `0`
- Fixtures valid for `MatchHistory`: `0` (`0%`)
- Events were available and included goals, cards, substitutions and VAR.
- Player-statistic rows for the sampled final: `0`
- Predictions returned one complete response for fixture `1316048`.
- Historical pre-match odds returned zero rows, consistent with the documented seven-day retention window.

All ten matches were missing corners, total shots, shots on goal and possession. They cannot be inserted as valid model history without another data source.

## Primera Division 2024 comparison

League ID `265` advertised fixture and player statistics. Fixture `1161556`, Universidad de Chile vs Everton de Vina, returned every required `MatchHistory` field:

```json
{
  "matchDate": "2024-11-10",
  "homeTeam": "Universidad de Chile",
  "awayTeam": "Everton de Vina",
  "homeFormation": "4-3-3",
  "awayFormation": "4-2-3-1",
  "homeGoals": 1,
  "awayGoals": 1,
  "homeCorners": 9,
  "awayCorners": 9,
  "homeShots": 21,
  "awayShots": 8,
  "homeShotsOnGoal": 8,
  "awayShotsOnGoal": 4,
  "homePossession": 58.0,
  "awayPossession": 42.0,
  "sourceMatchId": "api-football-1161556"
}
```

This comparison passed all local `MatchHistory` validation rules.

## Relevant endpoint matrix

| Endpoint | Tested | Result |
| --- | --- | --- |
| `/status` | Yes | Free plan, 100 requests/day, 10 requests/minute |
| `/leagues` | Yes | Chilean leagues and per-season coverage returned |
| `/teams` | Yes | 75 Copa Chile 2024 teams returned |
| `/fixtures` | Yes | Results and schedules returned |
| `/fixtures/statistics` | Yes, 10 fixtures | Empty for Copa Chile; complete for Primera Division comparison |
| `/fixtures/lineups` | Yes, 10 fixtures | Rows returned for Copa Chile but formations were null |
| `/fixtures/events` | Yes | Goals, cards, substitutions and VAR returned |
| `/fixtures/players` | Yes | Empty for sampled Copa Chile fixture |
| `/predictions` | Yes | One prediction returned for Copa Chile fixture `1316048` |
| `/odds` | Yes | Endpoint accessible; old fixture empty because odds history is limited |

A separate current-date odds request returned bookmakers and corner markets including `Corners Over Under`, `Corners 1x2`, `Corners Asian Handicap`, `Home Corners Over/Under`, `Away Corners Over/Under`, and first/second-half corner totals. This confirms endpoint access, but not Copa Chile market coverage for a specific future fixture.

## Conclusion

API-Football is suitable as a historical source for competitions whose season coverage has `statistics_fixtures: true`, such as Chilean Primera Division. It is not sufficient by itself for Copa Chile, Primera B, Segunda Division or Super Cup seasons that advertise `statistics_fixtures: false`. The Free plan also cannot populate current 2026 history because it is limited to seasons 2022-2024.
