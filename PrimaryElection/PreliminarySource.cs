using static League.FormMain;

namespace League.PrimaryElection
{
    /// <summary>What the preselection window reads; icons it returns belong to the caller, which disposes them.</summary>
    public interface IPreliminarySource
    {
        Task<List<PrimaryChampion>> LoadChampionsAsync(CancellationToken cancellationToken);

        Task<Image?> LoadIconAsync(int championId, CancellationToken cancellationToken);
    }

    /// <summary>The champion list and icons from the shared resource loader.</summary>
    public sealed class LcuPreliminarySource : IPreliminarySource
    {
        public async Task<List<PrimaryChampion>> LoadChampionsAsync(CancellationToken cancellationToken)
        {
            var champions = Globals.resLoading.ChampionManager;
            await champions.InitializeAsync(forceRefresh: false);
            cancellationToken.ThrowIfCancellationRequested();
            return champions.AllChampions.Where(c => c.IsActive).OrderBy(c => c.Name).ToList();
        }

        // The shared cache owns its image; the window keeps its own copy.
        public async Task<Image?> LoadIconAsync(int championId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shared = await Globals.resLoading.GetChampionIconAsync(championId);
            return shared == null ? null : new Bitmap(shared);
        }
    }
}
