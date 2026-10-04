using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32;

namespace OmenLiteControl
{
    internal enum DriverState
    {
        Available,
        Missing,
        UpdateRequired,
        Unavailable
    }

    internal sealed class DriverInfo
    {
        internal uint? Version;
        internal string InstalledVersion, InstallLocation, DriverPath, MetadataError;
        internal int Error;

        internal static string VersionText(uint version)
        {
            return (version >> 16) + "." + ((version >> 8) & 255) + "." + (version & 255);
        }

        internal static string ResolveDriverPath(string value)
        {
            string path = Environment.ExpandEnvironmentVariables(value ?? "").Trim().Trim('"');
            const string root = @"\SystemRoot\";
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                    path.Substring(root.Length));
            if (path.StartsWith(@"\??\", StringComparison.Ordinal))
                path = path.Substring(4);
            if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                    path);
            return path;
        }

        internal string Detail(bool english)
        {
            string missing = english ? "Unavailable" : "未读取到";
            string text = (english ? "Driver version (device): " : "驱动版本（设备回读）：") +
                          (Version.HasValue ? VersionText(Version.Value) : missing) +
                          Environment.NewLine + (english ? "Installed version: " : "安装版本：") +
                          (String.IsNullOrEmpty(InstalledVersion) ? missing : InstalledVersion) +
                          Environment.NewLine + (english ? "Installation folder: " : "安装目录：") +
                          (String.IsNullOrEmpty(InstallLocation) ? missing : InstallLocation) +
                          Environment.NewLine + (english ? "Driver file: " : "驱动文件：") +
                          (String.IsNullOrEmpty(DriverPath) ? missing : DriverPath);
            if (Error != 0)
                text += Environment.NewLine + (english ? "Device error: " : "设备访问错误：") +
                        Error + " — " + new Win32Exception(Error).Message;
            if (!String.IsNullOrEmpty(MetadataError))
                text += Environment.NewLine + (english ? "Installation details: " : "安装信息：") +
                        MetadataError;
            return text;
        }
    }

    internal static class DriverSetup
    {
        internal const string InstallerHash =
            "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
        internal const uint MinimumVersion = 0x00020200;
        internal static DriverInfo LastProbe;

        internal static DriverState Classify(bool open, uint version, int error, bool registered)
        {
            if (open)
                return version >= MinimumVersion ? DriverState.Available
                                                 : DriverState.UpdateRequired;
            return !registered && (error == 2 || error == 3) ? DriverState.Missing
                                                             : DriverState.Unavailable;
        }

        internal static DriverState Probe()
        {
            int error;
            uint version;
            bool open = PawnEcPorts.Probe(out version, out error);
            var info = new DriverInfo { Version = open ? (uint?)version : null, Error = error };
            bool registered = true; // An unreadable registry must not be reported as absent.
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                           @"SYSTEM\CurrentControlSet\Services\PawnIO"))
                {
                    registered = key != null;
                    if (key != null)
                        info.DriverPath = DriverInfo.ResolveDriverPath(
                            Convert.ToString(key.GetValue("ImagePath")));
                }
                using (var key = Registry.LocalMachine.OpenSubKey(
                           @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
                {
                    if (key != null)
                    {
                        info.InstalledVersion = Convert.ToString(key.GetValue("DisplayVersion"));
                        info.InstallLocation = Convert.ToString(key.GetValue("InstallLocation"));
                    }
                }
            }
            catch (System.Security.SecurityException e)
            {
                info.MetadataError = e.Message;
            }
            catch (UnauthorizedAccessException e)
            {
                info.MetadataError = e.Message;
            }
            catch (IOException e)
            {
                info.MetadataError = e.Message;
            }
            LastProbe = info;
            return Classify(open, version, error, registered);
        }

        internal static bool MatchesInstaller(Stream stream)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", "") == InstallerHash;
        }

        // Called only from the user's explicit enable/update button, never on startup or --status.
        internal static bool Install()
        {
            HardwareStatus.RequireSupportedBoard();
            using (var gate = new Mutex(false, @"Global\OMENLite.PawnIO.Setup"))
            {
                bool held = false;
                try
                {
                    try
                    {
                        held = gate.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        held = true;
                    }
                    if (!held)
                        throw new InvalidOperationException("Another driver setup is in progress.");
                    DriverState state = Probe();
                    if (state == DriverState.Available)
                        return false;
                    if (state == DriverState.Unavailable)
                        throw new InvalidOperationException(
                            "PawnIO exists but is unavailable. Check administrator permissions or restart Windows; it will not be reinstalled automatically.");

                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "driver",
                                               "PawnIO_setup.exe");
                    // Keep the checked package locked against writes/replacement while launching.
                    using (var stream =
                               new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (!MatchesInstaller(stream))
                            throw new InvalidDataException(
                                "PawnIO installer integrity check failed. Extract the original complete package again.");
                        var info = new ProcessStartInfo(path, "-install -silent") {
                            UseShellExecute = false, CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            WorkingDirectory = Path.GetDirectoryName(path)
                        };
                        using (var process = Process.Start(info))
                        {
                            process.WaitForExit();
                            if (process.ExitCode == 3010)
                                return true; // No automatic restart.
                            if (process.ExitCode != 0)
                                throw new Win32Exception(process.ExitCode,
                                                         "PawnIO installation failed.");
                        }
                    }
                    for (int i = 0; i < 20; i++)
                    {
                        if (Probe() == DriverState.Available)
                            return false;
                        Thread.Sleep(250);
                    }
                    throw new InvalidOperationException(
                        "Setup finished, but PawnIO is not available yet. Restart Windows and refresh.");
                }
                finally
                {
                    if (held)
                        gate.ReleaseMutex();
                }
            }
        }
    }
}
