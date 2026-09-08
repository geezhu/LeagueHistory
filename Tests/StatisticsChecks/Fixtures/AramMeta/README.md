# arammeta aggregate response fixtures

Source: https://github.com/Lanternko/ARAM-Mayhem-Database/tree/main/docs/api
Website: https://arammeta.com/ (project README states MIT).
Captured September 9, 2026; patch 16.17, detailVersion 20260908-1788870236.

index.json retains three champions, three recommendations per rarity and their augment metadata. 103.json retains three recommendations in each equipment/spell category. These are public aggregate statistics, not player records. Names, rates and fields remain source values. Test HTTP handlers do not upload data or contact live services.

Tier cuts follow scripts/tierlist_engine.py: adjusted win rate >= .55 OP, .52 T1, .50 T2, .48 T3, .46 T4, otherwise T5. Preserve wr vs rawWr rather than substituting one for the other.
