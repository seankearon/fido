using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Fido.Views;

/// <summary>
/// Re-enables the native window <em>system menu</em> on <c>Alt+Space</c>.
///
/// With full window decorations the OS draws the title bar and its system menu — Move, Size,
/// Minimize, Maximize, Close — reachable from the title-bar icon or a title-bar right-click. The
/// keyboard shortcut, though, is dead: Avalonia's Win32 backend raises <c>Alt+Space</c> as a managed
/// key event and its own window procedure then <em>swallows</em> the <c>WM_SYSCOMMAND(SC_KEYMENU)</c>
/// that would drop the menu — it returns early whenever the lParam high word is zero, which is exactly
/// the keyboard-mnemonic case (regression AvaloniaUI/Avalonia#6592). So replaying <c>SC_KEYMENU</c> is
/// futile: the message loops straight back into Avalonia and is discarded.
///
/// Instead we do what WPF's <c>SystemCommands.ShowSystemMenu</c> does — pop the real system menu with
/// <see cref="TrackPopupMenuEx"/> (mouse-mode, so it isn't coupled to the Alt/Space key state and
/// can't self-dismiss), let it return the chosen command, then <see cref="PostMessage"/> that
/// <em>concrete</em> command (<c>SC_MOVE</c> / <c>SC_CLOSE</c> / …, never <c>SC_KEYMENU</c>), which
/// Avalonia forwards to the default window procedure as usual. The whole thing is deferred onto the
/// dispatcher so the key dispatch fully unwinds before the menu's nested modal loop starts. It is a
/// no-op off Windows, where the gesture has no Win32 system menu.
/// </summary>
internal static class SystemMenu
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint TPM_LEFTBUTTON = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;

    /// <summary>Wires <c>Alt+Space</c> on <paramref name="window"/> to open its system menu.</summary>
    public static void EnableAltSpace(Window window) =>
        // Tunnel so the window sees the gesture before any focused child; handledEventsToo in case a
        // child marks it handled first. Space isn't text input while Alt is down, so nothing else wants it.
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.Alt) return;
        if (!OperatingSystem.IsWindows()) return;   // no Win32 system menu elsewhere — leave the gesture alone
        if (sender is not Window window) return;

        var handle = window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;

        // Mark handled to suppress Avalonia's default handling and the system "ding", then defer the
        // menu: opening it synchronously here would spin its modal loop on top of the still-unwinding
        // WM_SYSKEYDOWN dispatch (re-entrancy), so we let the key message finish first.
        e.Handled = true;
        var hwnd = handle.Handle;
        Dispatcher.UIThread.Post(() => Show(hwnd), DispatcherPriority.Input);
    }

    /// <summary>Pops the native system menu at the window's title bar and dispatches the chosen command.</summary>
    private static void Show(nint hwnd)
    {
        if (!OperatingSystem.IsWindows()) return;

        var menu = GetSystemMenu(hwnd, bRevert: false);
        if (menu == IntPtr.Zero) return;
        if (!GetWindowRect(hwnd, out var rect)) return;

        // A popup menu only tracks correctly for the foreground window (KB135788); the window that
        // just received the key already is, so this is a cheap safety belt.
        SetForegroundWindow(hwnd);

        // TPM_RETURNCMD returns the selected command instead of auto-posting it, so we control re-entry.
        var cmd = TrackPopupMenuEx(menu, TPM_LEFTBUTTON | TPM_RETURNCMD, rect.Left, rect.Top, hwnd, IntPtr.Zero);
        if (cmd == 0) return;   // dismissed without a choice

        // A concrete command (SC_MOVE / SC_SIZE / SC_CLOSE / …) — never SC_KEYMENU — so Avalonia's
        // WM_SYSCOMMAND filter doesn't match it and it reaches DefWindowProc. Posted, not sent, to
        // keep the resulting work off this stack.
        PostMessage(hwnd, WM_SYSCOMMAND, (nint)cmd, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetSystemMenu(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
