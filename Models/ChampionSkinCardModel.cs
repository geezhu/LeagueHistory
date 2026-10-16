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
        public List<SkinThumbItem> Skins { get; set; } = new();
        public bool HasNonDefaultSkin => Skins != null && Skins.Any(s => !s.IsBase && s.SkinId % 1000 != 0);
    }

    public class SkinThumbItem
    {
        public int SkinId { get; set; }
        public string Name { get; set; } = "";
        public bool IsBase { get; set; }
        public string? ImagePath { get; set; } // 本地缓存路径，可空

        ///
        /// <summary>LCU 返回的 tile 相对路径，可空
        /// </summary>
        public string? TilePath { get; set; }
    }
}
