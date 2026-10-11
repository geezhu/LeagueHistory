using System.Diagnostics;
using System.Runtime.InteropServices;

namespace League.Services;

/// <summary>Keep the assistant in the client's owned-window group without global topmost state.</summary>
public sealed class AssistantWindowLayer : IDisposable
{
    private readonly Form _form;
    private IntPtr _client;
    private int _lastError;
    private bool _disposed;

    public AssistantWindowLayer(Form form)
    {
        _form = form;
        _form.HandleCreated += HandleCreated;
    }

    public bool Attach(IntPtr client)
    {
        if (_disposed || _form.IsDisposed || client == IntPtr.Zero || !IsWindow(client))
        {
            return false;
        }

        _client = client;
        if (_form.TopMost)
        {
            _form.TopMost = false;
        }

        return ApplyOwner(_form.Handle);
    }

    private void HandleCreated(object? sender, EventArgs e)
    {
        // WinForms may recreate its HWND after a DPI/style change or a client restart.
        if (_client == IntPtr.Zero || !IsWindow(_client))
        {
            return;
        }

        IntPtr window = _form.Handle,
            client = _client;
        ApplyOwner(window);
        // Form.RecreateHandle restores its managed owner after HandleCreated returns.
        // Reapply after that completes, rejecting queued work for an old HWND/client.
        _form.BeginInvoke(
            new Action(() =>
            {
                if (
                    !_disposed
                    && !_form.IsDisposed
                    && _form.IsHandleCreated
                    && _form.Handle == window
                    && _client == client
                    && IsWindow(client)
                )
                {
                    ApplyOwner(window);
                }
            })
        );
    }

    private bool ApplyOwner(IntPtr window)
    {
        if (window == _client)
        {
            return false;
        }

        if (GetWindow(window, 4) == _client)
        {
            return true; // GW_OWNER
        }
        // Only change our own top-level window; never reparent or modify the LOL window.
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr(window, -8, _client);
        }
        else
        {
            SetWindowLong(window, -8, _client.ToInt32());
        }

        int error = Marshal.GetLastWin32Error();
        if (GetWindow(window, 4) == _client)
        {
            _lastError = 0;
            return true;
        }
        if (error != _lastError)
        {
            Debug.WriteLine($"Assistant window owner failed: {error}");
        }

        _lastError = error;
        return false;
    }

    public void Detach()
    {
        if (_client == IntPtr.Zero)
        {
            return;
        }

        if (!_form.IsDisposed && _form.IsHandleCreated && GetWindow(_form.Handle, 4) == _client)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr(_form.Handle, -8, IntPtr.Zero);
            }
            else
            {
                SetWindowLong(_form.Handle, -8, 0);
            }
        }
        _client = IntPtr.Zero;
    }

    public void Dispose()
    {
        _disposed = true;
        _form.HandleCreated -= HandleCreated;
        Detach();
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
