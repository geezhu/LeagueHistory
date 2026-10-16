using System.Diagnostics;

namespace League.Clients;

public class LcuConnector
{
    public async Task<(bool success, string port, string token)> InitializeAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            var result = await Task.Run(() =>
            {
                bool success = LcuProcessDiscovery.TryFind(out var port, out var token);
                return (success, port, token);
            });
            if (result.success)
            {
                return result;
            }

            await Task.Delay(1000);
        }
        return (false, "", "");
    }
}
