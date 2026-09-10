namespace League.Services;

public static class GameWindowIdentity
{
    public static bool Matches(uint foregroundId,int processId,string processName)=>foregroundId>0
        && foregroundId==processId && processName.Equals("League of Legends",StringComparison.OrdinalIgnoreCase);
}
