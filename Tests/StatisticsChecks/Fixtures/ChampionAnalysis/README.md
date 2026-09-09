# CommunityDragon extraction fixtures

Captured from `https://raw.communitydragon.org/latest/game/data/characters/{alias}/{alias}.bin.json` during the 2026-09-09 investigation, for lux, morgana and nautilus.

These are reduced snapshots: the character root and the four referenced spell `mSpell` objects are retained unchanged. They do not establish an exact game patch; reports must flag `latest` as unpinned. Regeneration is deliberate, never an automatic network step in tests.

Run the offline verifier and export Markdown/JSON evidence reports:

```powershell
dotnet run --project Tests/StatisticsChecks/StatisticsChecks.csproj -c Release --no-restore -- --champion-analysis bin/champion-analysis-reports
```

The verifier currently extracts data and symbolic formulas. It does not claim general control classification, effective range, mode adjustments, or formula evaluation with unverified stat enums.
