# Home corners feature contract

Status: **active contract / Home Corners 2026**. Version
`home-corners-2026-08-09-trial-1840` declares exactly the 60 features below. Together these
60 fields are also the complete union required by the other 11 Models 2026 bundles; those
models select smaller ordered subsets from the same server-built dictionary. The API selects
them in metadata order; unexpected fields, missing fields and every `Target*` field are
rejected. The official runtime converts categorical nulls to `Unknown` and imputes numeric
nulls exclusively with the 55 medians declared by `model_metadata.json`.

All history is queried through the same SQL-backed prediction context used by the current
webapp, deduplicated, ordered newest-first, and restricted to `MatchDate` strictly before
the requested match. The production run was trained on male senior matches; this first
target therefore requests `teamGender=M` internally.

| Feature | Type | Source | Null | Transformation | Status |
|---|---|---|---|---|---|
| IsKnockout | numeric/int | Match input | no | Boolean to 0/1 | available |
| HomeHistoricBig3 | numeric/int | `sp_GetTeamsByLeague` via team-info repository | no | Exact/canonical team match; Boolean to 0/1 | available |
| AwayHistoricBig3 | numeric/int | `sp_GetTeamsByLeague` via team-info repository | no | Exact/canonical team match; Boolean to 0/1 | available |
| HomeAvgCornersFor5 | numeric | Home general history | no after minimum history validation | Mean of corners for, newest 5 | available |
| HomeAvgCornersFor10 | numeric | Home general history | no after minimum history validation | Mean of corners for, newest 10 available | available |
| HomeMedianCornersFor10 | numeric | Home general history | no after minimum history validation | Median of corners for, newest 10 available | available |
| HomeAvgCornersAgainst10 | numeric | Home general history | no after minimum history validation | Mean opponent corners, newest 10 available | available |
| HomeStdDevCornersFor10 | numeric | Home general history | yes when fewer than 2 | Sample standard deviation (`n-1`) | available |
| HomeAvgShotsFor5 | numeric | Home general history | no after minimum history validation | Mean shots for, newest 5 | available |
| HomeAvgShotsFor10 | numeric | Home general history | no after minimum history validation | Mean shots for, newest 10 available | available |
| HomeMedianShotsFor10 | numeric | Home general history | no after minimum history validation | Median shots for, newest 10 available | available |
| HomeAvgShotsAgainst10 | numeric | Home general history | no after minimum history validation | Mean opponent shots, newest 10 available | available |
| HomeAvgShotsOnGoalFor5 | numeric | Home general history | no after minimum history validation | Mean SOG for, newest 5 | available |
| HomeAvgShotsOnGoalFor10 | numeric | Home general history | no after minimum history validation | Mean SOG for, newest 10 available | available |
| HomeMedianShotsOnGoalFor10 | numeric | Home general history | no after minimum history validation | Median SOG for, newest 10 available | available |
| HomeAvgShotsOnGoalAgainst10 | numeric | Home general history | no after minimum history validation | Mean opponent SOG, newest 10 available | available |
| HomeShotAccuracy10 | numeric | Home general history | yes if total shots is zero | Sum SOG divided by sum shots | available |
| HomeAvgPossession10 | numeric | Home general history | no in current DTO | Mean team possession, newest 10 available | available; null fidelity must be rechecked against production fixture |
| HomeAvgGoalsFor5 | numeric | Home general history | no after minimum history validation | Mean goals for, newest 5 | available |
| HomeAvgGoalsFor10 | numeric | Home general history | no after minimum history validation | Mean goals for, newest 10 available | available |
| HomeMedianGoalsFor10 | numeric | Home general history | no after minimum history validation | Median goals for, newest 10 available | available |
| HomeAvgGoalsAgainst10 | numeric | Home general history | no after minimum history validation | Mean opponent goals, newest 10 available | available |
| HomePointsPerMatch5 | numeric | Home general history | no after minimum history validation | Win=3, draw=1, loss=0; mean newest 5 | available |
| HomePointsPerMatch10 | numeric | Home general history | no after minimum history validation | Win=3, draw=1, loss=0; mean newest 10 available | available |
| HomeDaysRest | numeric/int | Home general history + requested date | no after minimum history validation | Calendar days since latest prior match | available |
| HomeAvgHomeCornersFor5 | numeric | Home-at-home history | yes | Mean home-team corners, newest 5 venue matches | available |
| HomeAvgHomeCornersAgainst5 | numeric | Home-at-home history | yes | Mean opponent corners, newest 5 venue matches | available |
| HomeAvgHomeGoalsFor5 | numeric | Home-at-home history | yes | Mean home-team goals, newest 5 venue matches | available |
| HomeAvgHomeGoalsAgainst5 | numeric | Home-at-home history | yes | Mean opponent goals, newest 5 venue matches | available |
| AwayAvgCornersFor5 | numeric | Away general history | no after minimum history validation | Mean corners for, newest 5 | available |
| AwayAvgCornersFor10 | numeric | Away general history | no after minimum history validation | Mean corners for, newest 10 available | available |
| AwayMedianCornersFor10 | numeric | Away general history | no after minimum history validation | Median corners for, newest 10 available | available |
| AwayAvgCornersAgainst10 | numeric | Away general history | no after minimum history validation | Mean opponent corners, newest 10 available | available |
| AwayStdDevCornersFor10 | numeric | Away general history | yes when fewer than 2 | Sample standard deviation (`n-1`) | available |
| AwayAvgShotsFor5 | numeric | Away general history | no after minimum history validation | Mean shots for, newest 5 | available |
| AwayAvgShotsFor10 | numeric | Away general history | no after minimum history validation | Mean shots for, newest 10 available | available |
| AwayMedianShotsFor10 | numeric | Away general history | no after minimum history validation | Median shots for, newest 10 available | available |
| AwayAvgShotsAgainst10 | numeric | Away general history | no after minimum history validation | Mean opponent shots, newest 10 available | available |
| AwayAvgShotsOnGoalFor5 | numeric | Away general history | no after minimum history validation | Mean SOG for, newest 5 | available |
| AwayAvgShotsOnGoalFor10 | numeric | Away general history | no after minimum history validation | Mean SOG for, newest 10 available | available |
| AwayMedianShotsOnGoalFor10 | numeric | Away general history | no after minimum history validation | Median SOG for, newest 10 available | available |
| AwayAvgShotsOnGoalAgainst10 | numeric | Away general history | no after minimum history validation | Mean opponent SOG, newest 10 available | available |
| AwayShotAccuracy10 | numeric | Away general history | yes if total shots is zero | Sum SOG divided by sum shots | available |
| AwayAvgPossession10 | numeric | Away general history | no in current DTO | Mean team possession, newest 10 available | available; null fidelity must be rechecked against production fixture |
| AwayAvgGoalsFor5 | numeric | Away general history | no after minimum history validation | Mean goals for, newest 5 | available |
| AwayAvgGoalsFor10 | numeric | Away general history | no after minimum history validation | Mean goals for, newest 10 available | available |
| AwayMedianGoalsFor10 | numeric | Away general history | no after minimum history validation | Median goals for, newest 10 available | available |
| AwayAvgGoalsAgainst10 | numeric | Away general history | no after minimum history validation | Mean opponent goals, newest 10 available | available |
| AwayPointsPerMatch5 | numeric | Away general history | no after minimum history validation | Win=3, draw=1, loss=0; mean newest 5 | available |
| AwayPointsPerMatch10 | numeric | Away general history | no after minimum history validation | Win=3, draw=1, loss=0; mean newest 10 available | available |
| AwayDaysRest | numeric/int | Away general history + requested date | no after minimum history validation | Calendar days since latest prior match | available |
| AwayAvgAwayCornersFor5 | numeric | Away-as-away history | yes | Mean away-team corners, newest 5 venue matches | available |
| AwayAvgAwayCornersAgainst5 | numeric | Away-as-away history | yes | Mean opponent corners, newest 5 venue matches | available |
| AwayAvgAwayGoalsFor5 | numeric | Away-as-away history | yes | Mean away-team goals, newest 5 venue matches | available |
| AwayAvgAwayGoalsAgainst5 | numeric | Away-as-away history | yes | Mean opponent goals, newest 5 venue matches | available |
| League | categorical/text | Match input, canonicalized by existing data flow | no | Trimmed text | available |
| HomeTeam | categorical/text | Match input, canonicalized/resolved by existing history flow | no | Trimmed text | available |
| AwayTeam | categorical/text | Match input, canonicalized/resolved by existing history flow | no | Trimmed text | available |
| HomeFormationStyle | categorical/text | Optional match formation, otherwise latest prior formation | no | Back line 5/6=`defensive`, 1–3=`aggressive`, otherwise `normal`; missing=`unknown` | available; parity mapping must be confirmed with fixture |
| AwayFormationStyle | categorical/text | Optional match formation, otherwise latest prior formation | no | Back line 5/6=`defensive`, 1–3=`aggressive`, otherwise `normal`; missing=`unknown` | available; parity mapping must be confirmed with fixture |

## Activation verification

All 60 contract fields have a backend source or an explicit pre-match calculation. Venue
history can legitimately be null and is then handled with the declared model median; it is
never replaced with zero. The five official prebuilt feature fixtures match the native CBM
with maximum absolute error `0.0` under the required `1e-8` tolerance. Formation-style and
possession calculations remain documented explicitly so future dataset changes can be
reviewed without a silent fallback.
