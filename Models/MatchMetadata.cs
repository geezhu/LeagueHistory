namespace League.Models
{
    public class MatchMetadata
    {
        public long GameId { get; set; }
        public int TeamId { get; set; }
        public Newtonsoft.Json.Linq.JObject? GameData { get; set; }

        /// <summary>The player's estimated ARAM: Mayhem score and MVP or SVP mark; null in other modes.</summary>
        public OwnMatchScore? Score { get; set; }
    }
}
