# WeGame honor icons

Taken on 2026-10-23 from the WeGame desktop client's League of Legends helper, which loads it from Tencent's public CDN. These are original sprite pixels, not redrawn icons.

- Sprite: the `.honor16` background image, embedded as a data URI in https://wegame.gtimg.com/g.26-r.c2d3c/helper/lol/assets/life-history-Cc_2l0VS.css (310 x 24 PNG; each icon 10 x 10, cells 26 px apart)
- Cells (`.honor16-*` in the same stylesheet): attack `0`, god `1`, hurt `2`, hurt2 `3`, kill `4`, kill3 `5`, kill4 `6`, kill5 `7`, leave `8`, money `9`, pull `10`, soldier `11`
- Badge list: https://wegame.gtimg.com/g.26-r.c2d3c/helper/lol/v2/assets/battle-detail-player-item.650cebbe.js (`honorList`, each item rendered as `<i class="...">`)

It replaces an older 16 x 16 web sprite (https://cdn.tgp.qq.com/lol/images/common/honor16.c34f6b6f98d22e92120b566617344ed1.png) with the same artwork, which carried a translucent black outline meant for dark pages and turned grey on a white one. Public access does not imply an open-source asset license. Artwork remains owned by its respective rights holders.

Used mappings (cell): assists `0`, godlike `1`, damage taken `2`, champion damage `3`, most kills `4` (a single bloodied sword), triple kill `5`, quadra kill `6`, penta kill `7`, gold `9`, turrets `10`, minions `11`. The icons are drawn 1:1, centred in a 16 x 16 badge slot.

WeGame's battle detail script (https://wegame.gtimg.com/g.26-r.c2d3c/helper/lol/v2/assets/life-battle-api.752a0712.js) maps its honours to these cells: `isLargestChampionsKilled` → kill (杀人最多不服来辩), `isLargestAssists` → attack, `isLargestGoldEarned` → money, `isLeaver` → leave, `isTripleKills` / `isQuadraKills` / `isPentaKills` → kill3 / kill4 / kill5, `isGodlike` → god (连杀超过8个，超神), `isLargestAllMinionsKilled` → soldier, `isLargestTurretsKilled` → pull, `isLargestTotalDamageTaken` → hurt, `isLargestTotalDamageDealt` and `isLargestTotalDamageDealtToChampions` → hurt2. Checked against the 112 sampled games: the most-kills mark went to the game's highest kills, ties included, for all 1070 players, and 超神 to every player whose largest killing spree was 8 or more and to none below.

The client maps server-provided battle tags to icons; it does not document how all those tags are awarded. This application explicitly labels non-kill badges as match-wide maxima calculated from complete participant fields, including ties. No AFK mark is inferred; MVP and SVP are, from the estimated score only (below). No double-kill badge is shown. Only the highest multi-kill badge is displayed, matching the client's display behavior.

## Team summary icons

`team-*.png` are the masks of `.icon-data1` to `.icon-data7`, embedded as data URIs in the same stylesheet (`life-history-Cc_2l0VS.css`). The battle detail script (https://wegame.gtimg.com/g.26-r.c2d3c/helper/lol/assets/battle-detail-base-f92d45d5.js) puts `icon-data1` to `icon-data6` before the team's kills, gold, turrets, inhibitors, Barons and dragons; `icon-data7` is taken as the Rift Herald from its artwork alone. Only the alpha carries the shape: WeGame tints them with its text colour, and the application with its own icon grey. Each is twice its displayed size (24 x 21 shown at 12 x 10) and is halved when drawn.

Files: kills `team-kda` (1), gold `team-gold` (2), turrets `team-tower` (3), inhibitors `team-inhibitor` (4), Baron `team-baron` (5), dragons `team-dragon` (6), Rift Herald `team-riftHerald` (7).

## MVP and SVP

`badge-mvp.png` and `badge-svp.png` are `.icon-mvp` and `.icon-svp` (28 x 12), embedded as data URIs in https://wegame.gtimg.com/g.26-r.c2d3c/helper/lol/assets/common-CZvmXLBN.css; the player item script draws them over the champion icon from `battleHonour.isMvp` / `isSvp`. In the 112 sampled games WeGame gave MVP to the best score of the winning team and SVP to the best of the losing team every time. The application applies that rule to its estimated ARAM: Mayhem score (Assets/Scores/SOURCES.md), so the marks are its own estimate: they match WeGame's in about 86% of games, and the right player is among the estimate's top two of the team in 97%. Other modes have no estimate and show neither mark.

## First blood

No verified first-blood icon was found in this set. First blood uses a locally drawn flat blood-drop symbol of the same 10 px size, explicitly separate from the WeGame sprite mappings.
