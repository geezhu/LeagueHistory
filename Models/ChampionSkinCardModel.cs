using League.PrimaryElection;

namespace League.Models
{
    public class ChampionSkinCardModel
    {
        public int ChampionId { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Alias { get; set; } = "";
        public bool IsOwned { get; set; }
        public int DefaultSkinId { get; set; }
        public int PreferredSkinId { get; set; }
        public List<ChampionPosition> Positions { get; set; } = new();
        public List<SkinThumbItem> Skins { get; set; } = new();
        public bool HasNonDefaultSkin => Skins != null && Skins.Any(s => !s.IsDefault);
        public int NonDefaultSkinCount => Skins?.Count(s => !s.IsDefault) ?? 0;
    }

    public class SkinThumbItem
    {
        public int SkinId { get; set; }
        public string Name { get; set; } = "";
        public bool IsBase { get; set; }

        /// <summary>The champion's own look: the client marks it, and its ID is the champion ID × 1000.</summary>
        public bool IsDefault => IsBase || SkinId % 1000 == 0;

        /// <summary>LCU 返回的 tile 相对路径，可空</summary>
        public string? TilePath { get; set; }

        /// <summary>LCU 返回的原画相对路径，可空</summary>
        public string? SplashPath { get; set; }
    }
}
