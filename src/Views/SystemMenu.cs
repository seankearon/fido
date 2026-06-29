using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Fido.Views;

/// <summary>
/// Re-enables the native window <em>system menu</em> on <c>Alt+Space</c>.
///
/// With full window decorations the OS draws the title bar and its system menu — Move, Size,
/// Minimize, Maximize, Close — reachable from the title-bar icon or a title-bar right-click.
/// Avalonia's Win32 backend, however, raises <c>Alt+Space</c> as an ordinary managed key event
/// instead of forwarding it to the default window procedure, so the usual keyboard shortcut never
/// drops the menu. Catching the gesture here and posting the same message Windows would have sent
/// restores it. It is a no-op off Windows, where the gesture has no Win32 system menu.
/// </summary>
internal static class SystemMenu
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const nint SC_KEYMENU = 0xF100;
    private const nint Space = 0x20;

    /// <summary>Wires <c>Alt+Space</c> on <paramref name="window"/> to open its system menu.</summary>
    public static void EnableAltSpace(Window window) =>
        // Tunnel so the window sees the gesture before any focused child; handledEventsToo in case a
        // child marks it handled first. Space isn't text input while Alt is down, so nothing else wants it.
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.Alt) return;
        if (sender is Window window && Show(window)) e.Handled = true;
    }

    /// <summary>Opens the window's native system menu; a no-op (returns false) anywhere but Windows.</summary>
    private static bool Show(Window window)
    {
        if (!OperatingSystem.IsWindows()) return false;

        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return false;

        // Replays exactly what pressing Alt+Space does: DefWindowProc turns WM_SYSKEYDOWN(VK_SPACE)
        // into WM_SYSCOMMAND(SC_KEYMENU, ' '), dropping the window menu at the title-bar icon.
        SendMessage(handle.Handle, WM_SYSCOMMAND, SC_KEYMENU, Space);
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);
}
