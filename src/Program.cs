// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland;

internal static class Program
{
    private const string MutexName = @"Local\DynamicIsland_SingleInstance_Mutex_5D2F9E82";

    /// <summary>Append to %AppData%\\DynamicIsland\\error.log (reset past 200 KB). Never throws.</summary>
    internal static void Log(Exception? ex) => Log(ex?.ToString() ?? "unknown error");

    internal static void Log(string msg)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "error.log");
            if (File.Exists(file) && new FileInfo(file).Length > 200_000) File.Delete(file);
            File.AppendAllText(file, $"{DateTime.Now:s} {msg}\n\n");
        }
        catch (Exception) { /* logging must never crash the app */ }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Log("=== Program.Main starting ===");
        try
        {
            Log($"Checking mutex {MutexName}...");
            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            Log($"Mutex createdNew = {createdNew}");
            if (!createdNew)
            {
                Log("Mutex already held. Waiting up to 500ms...");
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromMilliseconds(500), false))
                    {
                        Log("Could not acquire mutex (another instance running). Exiting.");
                        return;
                    }
                    Log("Acquired mutex after wait.");
                }
                catch (AbandonedMutexException)
                {
                    Log("Acquired abandoned mutex.");
                }
            }

            Log("Configuring WPF Timeline frame rate...");
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(120));

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) => { Log($"DispatcherUnhandledException: {e.Exception}"); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log($"AppDomain UnhandledException: {e.ExceptionObject}");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => { Log($"TaskScheduler UnobservedTaskException: {e.Exception}"); e.SetObserved(); };

            Log("Constructing MainWindow...");
            var mainWindow = new MainWindow();
            Log("MainWindow constructed. Calling app.Run()...");
            app.Run(mainWindow);
            Log("app.Run() returned cleanly.");
        }
        catch (Exception ex)
        {
            Log($"FATAL exception in Main: {ex}");
        }
    }
}
