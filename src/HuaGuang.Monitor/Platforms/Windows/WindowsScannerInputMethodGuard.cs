using System.Runtime.InteropServices;
using HuaGuang.Monitor.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace HuaGuang.Monitor.Platforms.Windows;

public sealed class WindowsScannerInputMethodGuard : IScannerInputMethodGuard
{
    const uint KlfActivate = 0x00000001;
    const string UsEnglishLayoutId = "00000409";

    readonly object _gate = new();
    IntPtr _savedLayout;
    int _depth;

    public IDisposable EnterEnglishInputMode(Entry? entry = null)
    {
        ConfigureEntry(entry);

        lock (_gate)
        {
            if (_depth == 0)
            {
                _savedLayout = GetCurrentKeyboardLayout();
                ActivateEnglishLayout();
            }

            _depth++;
            return new RestoreScope(this);
        }
    }

    static void ConfigureEntry(Entry? entry)
    {
        if (entry?.Handler?.PlatformView is not TextBox textBox)
        {
            return;
        }

        textBox.InputScope = new InputScope
        {
            Names =
            {
                new InputScopeName(InputScopeNameValue.Default),
                new InputScopeName(InputScopeNameValue.AlphanumericHalfWidth)
            }
        };
    }

    void Restore()
    {
        lock (_gate)
        {
            _depth--;
            if (_depth > 0)
            {
                return;
            }

            if (_savedLayout != IntPtr.Zero)
            {
                ActivateKeyboardLayout(_savedLayout, 0);
            }

            _savedLayout = IntPtr.Zero;
        }
    }

    static IntPtr GetCurrentKeyboardLayout()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return GetKeyboardLayout(0);
        }

        var threadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        return GetKeyboardLayout(threadId);
    }

    static void ActivateEnglishLayout()
    {
        var english = LoadKeyboardLayout(UsEnglishLayoutId, KlfActivate);
        if (english != IntPtr.Zero)
        {
            ActivateKeyboardLayout(english, 0);
        }
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);

    sealed class RestoreScope : IDisposable
    {
        readonly WindowsScannerInputMethodGuard _owner;
        bool _disposed;

        public RestoreScope(WindowsScannerInputMethodGuard owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Restore();
        }
    }
}
