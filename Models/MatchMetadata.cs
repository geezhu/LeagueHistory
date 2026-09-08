namespace League.Models
{
    public class MatchMetadata
    {
        public long GameId { get; set; }
        public int TeamId { get; set; }
        public Newtonsoft.Json.Linq.JObject? GameData { get; set; }
    }
}
