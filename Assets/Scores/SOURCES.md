# ARAM: Mayhem score estimate

`aram-mayhem-score.json` is the model behind the estimated score on the scoreboard of ARAM: Mayhem games (queues 2400 and 3270). It approximates the score WeGame shows in its battle detail page ("评价"); it is not WeGame's formula and not WeGame's data.

## How it was made

- Sample: 112 ARAM: Mayhem games (1075 players) on patch 16.21, taken from the WeGame client's own cache of battle details (`GetBattleDetail`: per-player stats and `gameScore`, shown divided by 10,000). Only numbers were kept; names and account ids were dropped.
- The stats the formula reads were checked against the League client's record of the same games (`/lol-match-history/v1/games/{gameId}`): kills, deaths, damage taken, champion damage and crowd control were equal for every player, gold within 7 for 30 of 1075.
- Each stat is compared within the game, which is what the scores follow:

  ```
  score = intercept
        + goldShareOfBest            × gold ÷ the game's best gold
        + deathsZScore               × (deaths − game mean) ÷ game standard deviation
        + damageTakenShareOfBest     × damage taken ÷ the game's best
        + championDamageShareOfBest  × champion damage ÷ the game's best
        + killsShareOfBest           × kills ÷ the game's best
        + crowdControlToEnemyAverage × crowd control time ÷ the enemy team's average
        + championOffsets[champion]  (0 for a champion the sample never saw)
  clamped to 0–20
  ```

- Weights: least squares with a light ridge. Champion offsets: each champion's mean residual, shrunk toward zero as `sum ÷ (count + 4)`.

## Accuracy

Measured on games left out of the fit (each game predicted by a model fitted on the other 111):

| | mean error | within 0.5 | within 1 | top score on the right player |
|---|---|---|---|---|
| this formula with champion offsets | 0.71 | 43% | 74% | 80% |
| formula alone | 0.80 | 40% | 68% | 73% |

Tried and left out because they added nothing once these terms were in: number of game-best stats, multikills, first blood, turret and objective damage, self-mitigated damage, healing on teammates, win or loss, and nonlinear models (gradient boosting, a small neural network).

WeGame's scores themselves are close to normal: mean 9.5, standard deviation 2.0, slightly compressed at both ends.

## Limits

- Fitted only on ARAM: Mayhem; other modes are not estimated.
- The offsets of champions seen only a few times are rough.
- A change to WeGame's formula is not noticed; refitting needs a new sample.
