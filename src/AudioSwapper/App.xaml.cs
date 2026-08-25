using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AudioSwapper;

public partial class App : Application
{
    /// <summary>
    /// Per-user, not global: two people signed into the same machine each get
    /// their own tray icon and their own default device.
    /// </summary>
    private const string InstanceMutexName = @"Local\AudioSwapper.SingleInstance";

    private Mutex? _instanceMutex;
    private TrayApp? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string command = ParseCommand(e.Args);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running. Hand the command to that instance rather than
            // starting a second tray icon, then step aside.
            Interop.CommandServer.TrySend(command);

            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _tray = new TrayApp(command);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Audio Swapper could not start.\n\n" + ex.Message,
                "Audio Swapper", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// Maps the command line to one of the commands the running instance
    /// understands. Anything unrecognised means "just start", so a stray
    /// argument never leaves the app doing nothing.
    /// </summary>
    private static string ParseCommand(string[] args)
    {
        foreach (string arg in args)
        {
            switch (arg.TrimStart('-', '/').ToLowerInvariant())
            {
                case "swap":
                case "toggle": return "swap";
                case "menu":
                case "settings": return "menu";
                case "quit":
                case "exit": return "quit";
            }
        }

        return "none";
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A failure while switching or repainting should not take the tray icon
        // down -- the app would vanish with no explanation and no way back
        // short of relaunching it.
        MessageBox.Show(
            "Something went wrong.\n\n" + e.Exception.Message,
            "Audio Swapper", MessageBoxButton.OK, MessageBoxImage.Warning);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
