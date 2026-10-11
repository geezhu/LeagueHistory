using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using League.Clients;
using League.Controls;

internal static class AramMetaViewLifecycleChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        public bool Closed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return send(token);
        }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    public static void Run()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        try
        {
            ClosingWhileImagesLoad();
            Console.WriteLine(
                "PASS: arammeta closing waits for shared artwork and cancellation callbacks before cleanup."
            );
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static T Field<T>(Control view, string name) =>
        (T)view.GetType().GetField(name, PrivateInstance)!.GetValue(view)!;

    private static object? Invoke(Control view, string method, params object?[] args) =>
        view.GetType().GetMethod(method, PrivateInstance)!.Invoke(view, args);

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready())
        {
            Application.DoEvents();
            Thread.Sleep(5);
            Check(DateTime.UtcNow < deadline, "Timed out waiting for the simulated arammeta worker.");
        }
        Application.DoEvents();
    }

    private static byte[] Artwork()
    {
        using var bitmap = new Bitmap(8, 8);
        bitmap.SetPixel(0, 0, Color.Red);
        using var output = new MemoryStream();
        bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    private static void ClosingWhileImagesLoad()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bytes = Artwork();
        using var imageHandler = new Handler(async token =>
        {
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        using var dataHandler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new AramMetaClient(dataHandler);
        var images = new HttpClient(imageHandler);
        var type = typeof(PlayerCardControl).Assembly.GetType("League.Controls.AramMetaView")!;
        using var view = (Control)
            Activator.CreateInstance(type, PrivateInstance, null, new object[] { client, images }, null)!;
        var url = "https://arammeta.com/assets/test-" + Guid.NewGuid().ToString("N") + ".png";
        var renderToken = (CancellationToken)Invoke(view, "NewRenderToken")!;
        var source = Field<CancellationTokenSource>(view, "_renderLifetime");
        var gate = Field<SemaphoreSlim>(view, "_imageGate");
        using var iconHost = new Panel();
        var first = (Task<Image?>)Invoke(view, "ImageAsync", url, "", 0)!;
        try
        {
            PumpUntil(() => imageHandler.Calls == 1);
            Check(
                gate.Wait(0, CancellationToken.None)
                    && gate.Wait(0, CancellationToken.None)
                    && gate.Wait(0, CancellationToken.None),
                "One shared image owns one gate slot."
            );
            var queued = (Task<Image?>)Invoke(view, "ImageAsync", url + "?queued", "", 0)!;
            var icon = (PictureBox)Invoke(view, "Icon", iconHost, url, "test", renderToken, 32, "", 0)!;
            AssertClosing(view, source, gate, imageHandler, dataHandler, first, queued, icon, release);
        }
        finally
        {
            release.TrySetResult();
            view.Dispose();
            PumpUntil(() => Field<Task>(view, "_cleanupWork").IsCompleted);
            DeleteArtworkCache(url);
        }
    }

    private static void AssertClosing(
        Control view,
        CancellationTokenSource source,
        SemaphoreSlim gate,
        Handler images,
        Handler data,
        Task<Image?> first,
        Task<Image?> queued,
        PictureBox icon,
        TaskCompletionSource release
    )
    {
        view.Dispose();
        var cleanup = Field<Task>(view, "_cleanupWork");
        Check(source.Token.IsCancellationRequested, "Closing cancels the retired render immediately.");
        Check(
            !cleanup.IsCompleted && !images.Closed && !data.Closed,
            "HTTP resources must outlive pending image work."
        );
        Check(
            gate.CurrentCount == 0 && !first.IsCompleted && !queued.IsCompleted,
            "Queued artwork retains its live gate."
        );
        gate.Release(3);
        release.SetResult();
        PumpUntil(() => cleanup.IsCompleted);
        Check(
            cleanup.IsCompletedSuccessfully && first.Result == null && queued.Result == null,
            "Late artwork is discarded."
        );
        Check(icon.Image == null && images.Calls == 1, "Closing prevents icon publication and queued downloads.");
        Check(
            images.Closed && data.Closed && SourceDisposed(source),
            "Cleanup releases sources and both HTTP clients."
        );
        try
        {
            gate.Wait(0, CancellationToken.None);
            throw new Exception("Image gate was not disposed after all workers ended.");
        }
        catch (ObjectDisposedException)
        {
            // Access is deliberately used to verify the final ownership boundary.
        }
    }

    private static bool SourceDisposed(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static void DeleteArtworkCache(string url)
    {
        var filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".png";
        File.Delete(Path.Combine(Application.StartupPath, "Assets", "arammeta", filename));
    }
}
