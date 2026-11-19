using Ryujinx.Ava.Remaster;
using Ryujinx.Common.Configuration;

string scratch = Path.Combine(Path.GetTempPath(), "sixguns-diagnostics-" + Guid.NewGuid().ToString("N"));
AppDataManager.BaseDirPath = scratch;
Directory.CreateDirectory(AppDataManager.KeysDirPath);
Directory.CreateDirectory(AppDataManager.LogsDirPath);
Directory.CreateDirectory(RemasterProfile.GameRoot);
int passed = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
try
{
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string redacted = DebugReport.Redact(home + "/private " + Environment.UserName + " " + Environment.MachineName + " 8.8.8.8 192.168.1.2 " + new string('a', 64) + " " + new string('b', 32) + " " + new string('c',40));
    Check(string.IsNullOrEmpty(home) || !redacted.Contains(home, StringComparison.OrdinalIgnoreCase), "home path redacted");
    Check(!redacted.Contains("8.8.8.8") && redacted.Contains("192.168.1.2"), "public IPv4 redacted; LAN address retained");
    Check(!redacted.Contains(new string('a',64)) && !redacted.Contains(new string('b',32)) && redacted.Contains(new string('c',40)), "key-shaped hex redacted; commit ids retained");
    if (Environment.UserName.Length >= 3) Check(!redacted.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase), "username redacted");
    if (Environment.MachineName.Length >= 3) Check(!redacted.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase), "computer name redacted");

    string log = Path.Combine(RemasterProfile.GameRoot, "log.txt");
    Check(DebugReport.GameProblem() == null, "missing game log tolerated");
    File.WriteAllText(log, "=== CRASH: synthetic test\n");
    Check(DebugReport.GameProblem() == "The game crashed.", "port crash detected");
    File.WriteAllText(log, "FATAL: cannot load from 8.8.8.8\n");
    Check(DebugReport.GameProblem()?.Contains("<ip removed>") == true, "fatal launch error detected and redacted");

    DebugReport.GameStarting("vulkan");
    var interrupted = DebugReport.ClosedWhileRunning();
    Check(interrupted.Closed && interrupted.Backend == "vulkan" && !interrupted.Confirmed, "interrupted startup marker detected");
    Check(!DebugReport.ClosedWhileRunning().Closed, "interruption hint consumed once");
    int stopped = 0;
    DebugReport.GameExited += () => stopped++;
    DebugReport.GameStarting("opengl");
    DebugReport.GameStopped();
    Check(stopped == 1 && !DebugReport.ClosedWhileRunning().Closed, "normal stop clears marker and notifies launcher");
    File.WriteAllText(Path.Combine(scratch,"remaster-game-running"), "date\nvulkan\nok\n");
    Check(DebugReport.ClosedWhileRunning().Confirmed, "working-backend marker preserved");

    File.WriteAllText(Path.Combine(AppDataManager.KeysDirPath,"prod.keys"), "PRIVATE_KEY_CONTENT_SENTINEL");
    File.WriteAllText(Path.Combine(RemasterProfile.GameRoot,"libAndroid.so"), "PRIVATE_GAME_CONTENT_SENTINEL");
    Directory.CreateDirectory(Path.Combine(RemasterProfile.GameRoot,"save"));
    File.WriteAllText(Path.Combine(RemasterProfile.GameRoot,"save","sample"), "PRIVATE_SAVE_CONTENT_SENTINEL");
    File.WriteAllText(Path.Combine(RemasterProfile.GameRoot,"log.prev.txt"), "previous log included");
    for (int i=0;i<3;i++)
    {
        string p=Path.Combine(AppDataManager.LogsDirPath,$"emu-{i}.log");
        File.WriteAllText(p,$"emulator sentinel {i}");File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddMinutes(i-3));
    }
    File.WriteAllText(log,"OMITTED_PREFIX\n" + new string('x', 2*1024*1024) + "\nCURRENT_TAIL\n8.8.8.8\n");
    string report = DebugReport.Build("0.1.2", "test-port", "test-firmware");
    Check(report.Contains("CURRENT_TAIL") && !report.Contains("OMITTED_PREFIX") && report.Length < 1700*1024, "large game log bounded and newest tail retained");
    Check(report.Contains("previous log included") && report.Contains("emulator sentinel 2") && report.Contains("emulator sentinel 1") && !report.Contains("emulator sentinel 0"), "previous game log and two newest emulator logs collected");
    Check(report.Contains("Sample mod") && report.Contains("0.1.2") && report.Contains("test-firmware"), "version, firmware and mod context included");
    Check(!report.Contains("PRIVATE_KEY_CONTENT_SENTINEL") && !report.Contains("PRIVATE_SAVE_CONTENT_SENTINEL") && !report.Contains("PRIVATE_GAME_CONTENT_SENTINEL"), "keys, saves and game file contents excluded");
    Check(!report.Contains("8.8.8.8") && report.Contains("<ip removed>"), "complete report redacted");
    File.WriteAllText(Path.Combine(scratch,DebugReport.FileName), report);
    Check(File.Exists(Path.Combine(scratch,DebugReport.FileName)), "report text can be saved to a file");
    Console.WriteLine($"{passed} diagnostic checks passed.");
}
finally
{
    DebugReport.GameStopped();
    Directory.Delete(scratch,true);
}
