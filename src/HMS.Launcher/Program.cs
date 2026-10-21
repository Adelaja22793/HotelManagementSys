using System.Diagnostics;
using HMS.Updater;

// The shortcut on each workstation points here. It starts the active desktop version named in current.json, or the
// previous one if the active folder is damaged. It runs nothing else: no arguments from the manifest, no scripts.
var layout = new InstallLayout(AppContext.BaseDirectory);
var target = layout.LaunchTarget();
if (target is null)
{
    Console.Error.WriteLine("No installed HMS version was found. Reinstall the application.");
    return 1;
}

Process.Start(new ProcessStartInfo(target)
{
    UseShellExecute = false,
    WorkingDirectory = Path.GetDirectoryName(target)!,
});
return 0;
