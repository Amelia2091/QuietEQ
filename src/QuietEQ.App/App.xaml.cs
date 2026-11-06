using System.IO;
using System.Windows;

namespace QuietEQ.App;

public partial class App : Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--probe")
        {
            try
            {
                var output = Path.GetFullPath(e.Args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var directory = DeviceService.ConfigDirectory();
                File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { configDirectory = directory, configHash = directory is null ? null : QuietEQ.Core.ConfigInstaller.ReadHash(directory), devices = DeviceService.Enumerate() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(Path.GetFullPath(e.Args[1]) + ".error.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        var isolated = e.Args.Contains("--demo") || e.Args.Contains("--smoke-test");
        var dataIndex = Array.IndexOf(e.Args, "--data-dir");
        if (dataIndex >= 0 && !isolated) { MessageBox.Show("--data-dir 仅可用于 --demo 或 --smoke-test 隔离模式。"); Shutdown(1); return; }
        var dataDir = isolated
            ? dataIndex >= 0 && dataIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[dataIndex + 1]) : Path.Combine(Path.GetTempPath(), "QuietEQ-demo", Guid.NewGuid().ToString("N"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuietEQ");
        mutex = new Mutex(true, "Local\\QuietEQ-" + QuietEQ.Core.ConfigCompiler.Hash(dataDir)[..16], out var created);
        if (!created) { MessageBox.Show("QuietEQ 已在运行。"); Shutdown(); return; }
        try
        {
            var window = new MainWindow(dataDir, isolated, e.Args.Contains("--smoke-test"));
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "QuietEQ 无法启动"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
