# OP.GG response samples

Read-only requests made on 2026-09-08 to `https://lol-api-champion.op.gg` returned version 16.17.

- `aram.json`: `/api/global/champions/aram/103/none?tier=all`
- `ranked.json`: `/api/global/champions/ranked/103/mid?tier=emerald_plus`
- `arena.json`: `/api/global/champions/arena/103?tier=all`
- `tiers.json`: `/api/global/champions/ranked?tier=emerald_plus`

Build arrays are shortened to two entries; the tier list keeps champions 1 and 103. These contain aggregate champion statistics, no player account data. The fixtures verify response compatibility without relying on network access.

Set `LEAGUE_OPGG_LIVE=1` when running StatisticsChecks to explicitly enable read-only live endpoint checks. Rune application checks always use a fake HttpMessageHandler.
