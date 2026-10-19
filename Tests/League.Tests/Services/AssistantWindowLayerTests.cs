using System.Runtime.InteropServices;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The layer that keeps the assistant inside the League client's owned-window group: it attaches to a client HWND
/// that has no managed owner, leaves the global topmost band, stacks itself beneath the application that covers a
/// background client, gives the owner up on detach and on dispose, survives client replacement and client shutdown,
/// and re-applies the owner after WinForms recreates the HWND.
/// The layer follows the live window <see cref="UiHost"/> runs its message loop on, or a form created on that same
/// STA thread; the client is a window of our own, never a running game client. Window order is read back with
/// GW_OWNER / GW_HWNDNEXT rather than pixels. This class replaces the ownership, stacking and rebinding checks of
/// Tests/StatisticsChecks/AssistantWindowChecks.cs.
/// </summary>
[Trait("Category", "UI")]
public class AssistantWindowLayerTests
{
    private const uint GwHwndNext = 2; // GW_HWNDNEXT
    private const uint GwOwner = 4; // GW_OWNER
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x0008;
    private const int NeverAWindow = unchecked((int)0xDEADBEEF);

    // ---------------------------------------------------------------- attaching to a client

    [Fact]
    public void Attach_ExternalClientWindow_ReturnsTrueAndSetsTheNativeOwner()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            using var layer = new AssistantWindowLayer(host);

            var attached = layer.Attach(client.Handle);

            Assert.True(attached, "An external HWND without a managed Form owner can be attached.");
            Assert.Equal(client.Handle, Owner(host.Handle));
            Assert.Null(host.Owner);
        });
    }

    [Fact]
    public void Attach_TopMostLayer_LeavesTheGlobalTopMostBand()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            host.TopMost = true;
            Assert.Equal(WsExTopmost, ExStyle(host.Handle) & WsExTopmost);
            using var layer = new AssistantWindowLayer(host);

            layer.Attach(client.Handle);

            Assert.False(host.TopMost);
            Assert.Equal(0, ExStyle(host.Handle) & WsExTopmost);
            Assert.Equal(client.Handle, Owner(host.Handle));
        });
    }

    [Fact]
    public void Attach_ZeroHandle_IsRejected()
    {
        UiHost.Run(host =>
        {
            using var layer = new AssistantWindowLayer(host);

            var attached = layer.Attach(IntPtr.Zero);

            Assert.False(attached);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(NeverAWindow)]
    public void Attach_HandleThatIsNotAWindow_IsRejected(int raw)
    {
        UiHost.Run(host =>
        {
            var handle = new IntPtr(raw);
            Assert.False(IsWindow(handle), "The fixture handle is not a window.");
            using var layer = new AssistantWindowLayer(host);

            var attached = layer.Attach(handle);

            Assert.False(attached);
        });
    }

    [Fact]
    public void Attach_DestroyedWindowHandle_IsRejected()
    {
        UiHost.Run(host =>
        {
            IntPtr dead;
            using (var doomed = new QuietForm())
            {
                doomed.Show();
                dead = doomed.Handle;
                doomed.Dispose();
            }

            Assert.False(
                IsWindow(dead),
                "The fixture destroyed the client window, so its handle is no longer a window."
            );
            using var layer = new AssistantWindowLayer(host);

            var attached = layer.Attach(dead);

            Assert.False(attached);
        });
    }

    [Fact]
    public void Attach_ItsOwnWindow_IsRejected()
    {
        UiHost.Run(host =>
        {
            using var layer = new AssistantWindowLayer(host);
            // The host carries WinForms' own hidden owner (it is shown with ShowInTaskbar = false), so only the
            // owner this call could have added is interesting.
            IntPtr before = Owner(host.Handle);

            var attached = layer.Attach(host.Handle);

            Assert.False(attached, "A window cannot be its own owner.");
            Assert.Equal(before, Owner(host.Handle));
        });
    }

    [Fact]
    public void Attach_DisposedLayer_IsRejected()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            var layer = new AssistantWindowLayer(host);
            layer.Attach(client.Handle);
            layer.Dispose();

            var attached = layer.Attach(client.Handle);

            Assert.False(attached);
        });
    }

    [Fact]
    public void Attach_DisposedWindow_IsRejected()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            using var window = new QuietForm();
            using var layer = new AssistantWindowLayer(window);
            window.Dispose();
            Assert.True(window.IsDisposed, "The fixture disposed the window the layer follows.");

            var attached = layer.Attach(client.Handle);

            Assert.False(attached);
        });
    }

    // ---------------------------------------------------------------- replacing and losing the client

    [Fact]
    public void Attach_SecondClient_RebindsWithoutRecreatingTheLayer()
    {
        UiHost.Run(host =>
        {
            using var first = new QuietForm();
            using var replacement = new QuietForm();
            first.Show();
            replacement.Show();
            using var layer = new AssistantWindowLayer(host);
            layer.Attach(first.Handle);
            Assert.Equal(first.Handle, Owner(host.Handle));

            var attached = layer.Attach(replacement.Handle);

            Assert.True(attached, "A new client handle rebinds the same layer.");
            Assert.Equal(replacement.Handle, Owner(host.Handle));
        });
    }

    [Fact]
    public void Attach_ClientDestroyed_LeavesTheLayerWindowAlive()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            using var layer = new AssistantWindowLayer(host);
            layer.Attach(client.Handle);

            client.Dispose();

            Assert.False(host.IsDisposed, "Native client shutdown must not dispose the reusable managed layer.");
            Assert.True(IsWindow(host.Handle), "The layer window outlives the client window.");
        });
    }

    // ---------------------------------------------------------------- stacking

    [Fact]
    public void StackAboveClient_CoveringWindow_StacksBeneathItAndAboveTheClient()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            using var cover = new QuietForm();
            client.Show();
            cover.Show();
            using var layer = new AssistantWindowLayer(host);
            Assert.True(layer.Attach(client.Handle), "The fixture attached the layer to the client window.");
            Raise(cover.Handle);
            RaiseWithoutOwnerZOrder(host.Handle); // The layer is now above the covering window.
            Assert.True(Above(host.Handle, cover.Handle), "The fixture put the layer above the covering window.");
            Assert.True(Above(cover.Handle, client.Handle), "The fixture put the covering window above the client.");

            layer.StackAboveClient();

            Assert.True(
                Above(cover.Handle, host.Handle),
                "A layer opened for a covered client stays beneath the application that covers it."
            );
            Assert.True(Above(host.Handle, client.Handle), "The layer stays directly above the client it follows.");
        });
    }

    [Fact]
    public void StackAboveClient_NothingCoversTheClient_RaisesTheLayerAboveIt()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            using var layer = new AssistantWindowLayer(host);
            layer.Attach(client.Handle);
            RaiseWithoutOwnerZOrder(client.Handle); // The client is on top with its layer left behind.
            Assert.True(Above(client.Handle, host.Handle), "The fixture put the client above its layer.");

            layer.StackAboveClient();

            Assert.True(Above(host.Handle, client.Handle), "An uncovered client is followed by its layer.");
        });
    }

    [Fact]
    public void StackAboveClient_DetachedLayer_LeavesTheClientAboveIt()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            using var layer = new AssistantWindowLayer(host);
            layer.Attach(client.Handle);
            layer.Detach();
            Raise(client.Handle);
            Assert.True(Above(client.Handle, host.Handle), "The fixture put the client above the detached layer.");

            layer.StackAboveClient();

            Assert.True(Above(client.Handle, host.Handle), "A layer without a client is left where it is.");
        });
    }

    // ---------------------------------------------------------------- giving the client up

    [Fact]
    public void Detach_AttachedLayer_ClearsTheNativeOwner()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            using var layer = new AssistantWindowLayer(host);
            layer.Attach(client.Handle);
            Assert.Equal(client.Handle, Owner(host.Handle));

            layer.Detach();

            Assert.Equal(IntPtr.Zero, Owner(host.Handle));
            Assert.False(host.IsDisposed, "Detaching leaves the reusable layer window alive.");
        });
    }

    [Fact]
    public void Detach_ClientAlreadyDestroyed_LeavesTheLayerReusable()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            using var layer = new AssistantWindowLayer(host);
            layer.Attach(client.Handle);
            client.Dispose();

            layer.Detach();
            using var restarted = new QuietForm();
            restarted.Show();

            var attached = layer.Attach(restarted.Handle);

            Assert.True(attached, "A layer whose client HWND was destroyed still serves a restarted client.");
            Assert.Equal(restarted.Handle, Owner(host.Handle));
            Assert.False(host.IsDisposed, "Detached layer survives client shutdown.");
        });
    }

    [Fact]
    public void Dispose_AttachedLayer_ClearsTheNativeOwner()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            var layer = new AssistantWindowLayer(host);
            layer.Attach(client.Handle);
            Assert.Equal(client.Handle, Owner(host.Handle));

            layer.Dispose();

            Assert.Equal(IntPtr.Zero, Owner(host.Handle));
        });
    }

    [Fact]
    public void Dispose_SecondCall_IsSafe()
    {
        UiHost.Run(host =>
        {
            var layer = new AssistantWindowLayer(host);
            layer.Dispose();

            var second = Record.Exception(layer.Dispose);

            Assert.Null(second);
        });
    }

    // ---------------------------------------------------------------- the window is recreated

    [Fact]
    public void RecreatedHandle_AttachedLayer_ReappliesTheNativeOwner()
    {
        UiHost.Run(host =>
        {
            using var client = new QuietForm();
            client.Show();
            using var window = new RecreatedForm();
            using var layer = new AssistantWindowLayer(window);
            window.Show();
            Assert.True(layer.Attach(client.Handle), "The fixture attached the layer to the client window.");
            IntPtr original = window.Handle;

            window.Recreate();
            Application.DoEvents();

            Assert.NotEqual(original, window.Handle);
            Assert.Equal(client.Handle, Owner(window.Handle));
        });
    }

    // ---------------------------------------------------------------- fixtures

    private static IntPtr Owner(IntPtr window) => GetWindow(window, GwOwner);

    private static int ExStyle(IntPtr window) => GetWindowLong(window, GwlExStyle);

    /// <summary>Whether the first of our own windows is above the second one in the z-order.</summary>
    private static bool Above(IntPtr first, IntPtr second)
    {
        for (
            IntPtr window = GetWindow(first, GwHwndNext);
            window != IntPtr.Zero;
            window = GetWindow(window, GwHwndNext)
        )
        {
            if (window == second)
            {
                return true;
            }
        }

        return false;
    }

    private static void Raise(IntPtr window) =>
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    /// <summary>
    /// Raises one of our own windows without dragging the window it owns along, the way the layer moves itself.
    /// </summary>
    private static void RaiseWithoutOwnerZOrder(IntPtr window) =>
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);

    /// <summary>An off-screen window that never takes the focus, so a check can own a real HWND of its own.</summary>
    private class QuietForm : Form
    {
        public QuietForm()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-16000, -16000);
            Size = new Size(320, 200);
            ShowInTaskbar = false;
        }

        protected override bool ShowWithoutActivation => true;
    }

    /// <summary>
    /// <see cref="Control.RecreateHandle"/> is protected, and the host <see cref="UiHost"/> hands out a plain
    /// <see cref="Form"/>, so a check that needs a recreated HWND asks for one from a form of its own.
    /// </summary>
    private sealed class RecreatedForm : QuietForm
    {
        public void Recreate() => RecreateHandle();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr after,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );
}
