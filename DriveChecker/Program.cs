using BigTyre.DiskMount.Configuration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using System.Net;
using System.Security;
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;
using System.Configuration;

namespace DriveChecker;

internal class Program
{
    public static Volumes Volume { get; set; }
    public static int CountUnlockingAttempt { get; set; } = 0;
    public static int UnlockingAttempts { get; set; }
    static async Task Main(string[] args)
    {
        
        Console.WriteLine("Hard Drive attacher started");

        AppDomain.CurrentDomain.ProcessExit += (sender, e) => Console.WriteLine("Hard Drive attacher exited");

        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            Console.WriteLine("Warning: This app must be run as Administrator.");
            return;
        }

        var settings = GetSettings(args);

        var targetDirectory = settings.TargetDirectory;
        var volumes = settings.Volumes;
        var checkInterval = TimeSpan.FromSeconds(settings.CheckFrequencyInSeconds);
        UnlockingAttempts = settings.UnlockingAttempts;
        bool RemapVolumes() => TryRemapVolumes(targetDirectory, volumes);

        while (true)
        {
            if(Volume == null) RemapVolumes();

            try
            {
                bool isEmpty = false;
                do
                {
                    //CheckBitLockerEncryptionLevel(targetDirectory);

                    Console.WriteLine($"Enumerating file system entries in {targetDirectory}");
                    var paths = Directory.EnumerateFileSystemEntries(targetDirectory);
                    isEmpty = !paths.Any();

                    if (isEmpty)
                    {
                        Console.WriteLine("Path is Empty! Alert!");
                        try
                        {
                            bool anyDriveMapped = RemapVolumes();
                            if (!anyDriveMapped)
                            {
                                await Task.Delay(1000);
                            }
                        }
                        catch (Exception)
                        {
                            await Task.Delay(1000);
                        }
                    }
                } while (isEmpty);
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine($"Directory '{targetDirectory}' not found.");
                RemapVolumes();
            }
            catch (IOException ex)
            {
                CatchBitLockerException(ex.Message);
            }
            catch (Exception ex) {
                Console.WriteLine(ex.Message);
            }

            await Task.Delay(checkInterval);
        }
    }

    private static void CatchBitLockerException(string message)
    {
        if(message.Contains("This drive is locked by BitLocker Drive Encryption") == true)
        {
            Console.WriteLine("This drive is locked by BitLocker Drive Encryption");
            string volumeDir;
            if (IsDriveNTFS(message))
            {
                volumeDir = getTargetDrive(message, "'", "'");
            }
            else
            {
                volumeDir = getTargetDrive(message, "'", ":") + ":";

            }
            Console.WriteLine(volumeDir);
            if (volumeDir != null)
            {
                DecryptBitLockerEncryption($"{volumeDir}");
            }
            else
            {
                Console.WriteLine("Failed to map the targe directory. Remapping");
            }
        } else { Console.WriteLine(message); }
    }

    private static bool IsDriveNTFS(string message)
    {
        bool isNtfs = false;
        string ntfsVolumeDrive = getTargetDrive(message, "'", "'");
            if(ntfsVolumeDrive != null && ntfsVolumeDrive.Length > 4) isNtfs = true;
        return isNtfs;
    }

    private static string getTargetDrive(string message, string start, string end)
    {
        if(message.Contains(start) && message.Contains(end))
        {
            int Start, End;
            Start = message.IndexOf(start, 0) + start.Length;
            End = message.IndexOf(end, Start);
            return message.Substring(Start, End - Start);
        }
        return "";
    }
    private static void CheckBitLockerEncryptionLevel(string targetDir)
    {
        try {
            Console.WriteLine("Checking BitLocker Encryption Level");
            //ToDo: update to the ntfs
            IShellProperty shellProperty = ShellObject.FromParsingName(targetDir).Properties.GetProperty("System.Volume.BitLockerProtection");
            
            if (shellProperty is ShellProperty<int?> intShellProperty)
            {
                Console.WriteLine("Property is shell property int");
                var value = intShellProperty.Value;
                Console.WriteLine($"Property value is {(value?.ToString() ?? "Null")}");
            }
            else
            {
                Console.WriteLine("Property is not shell property int");
            }

            int? bitLockerEncrptionStatus = (shellProperty as ShellProperty<int?>).Value;
            
            Console.WriteLine($"{bitLockerEncrptionStatus}" ?? "Null");
            if (bitLockerEncrptionStatus.HasValue)
            {
                /* 1: BitLocker On
                 * 3: BitLocker Encrypting
                 * 5: BitLocker Suspended
                 * 6: BitLocker On (Locked)
                 */

                if (bitLockerEncrptionStatus == 1 || bitLockerEncrptionStatus == 3 || bitLockerEncrptionStatus == 5)
                {
                    Console.WriteLine(bitLockerEncrptionStatus);
                }

                if (bitLockerEncrptionStatus == 6)
                {
                    DecryptBitLockerEncryption(targetDir);
                }
            }
        }
        catch (Exception ex)
        {
            //Console.WriteLine("Cannot find the drive");
            Console.WriteLine(ex.Message);
        }
    }

    private static void DecryptBitLockerEncryption(string targetDir)
    { 
        string password = Volume.BitLockerPassword;
        if(String.IsNullOrEmpty(password)) { Console.WriteLine("Password is empty in the App Settings. Please update and restart the service."); return; }
        
        Console.WriteLine("Unlocking the Drive");
        if(CountUnlockingAttempt >= UnlockingAttempts)
        {
            Console.WriteLine($"Attempt {CountUnlockingAttempt}: Failed to unlock the driver due to wrong credentials. Please update the App Settings");
        }
        
        string secureString = $"$SecureString = ConvertTo-SecureString '{password}' -AsPlainText -Force";
        string mountPoint = $"Unlock-BitLocker -MountPoint \"{targetDir}\" -Password $SecureString";
        string unlockBitLocker = secureString + "; " + mountPoint;

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
          FileName = "powershell.exe",
          Arguments = unlockBitLocker,
          UseShellExecute = true,
          Verb = "runas",
          CreateNoWindow = true,
          WindowStyle = ProcessWindowStyle.Hidden,
        };

        var process = Process.Start(startInfo);
        process.WaitForExit();
        CountUnlockingAttempt++;
    }

    private static AppSettings GetSettings(string[] args)
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddJsonFile("appSettings.json", optional: true);
        configBuilder.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appSettings.json"), optional: true);
        configBuilder.AddCommandLine(args);
#if DEBUG
        configBuilder.AddUserSecrets<Program>();
#endif 
        var config = configBuilder.Build();

        var settings = new AppSettings();
        config.Bind(settings);

        return settings;
    }

    static bool TryRemapVolumes(string targetDirectory, List<Volumes> volumes)
    {
        Console.WriteLine($"Attempting to remap volumes.");

        UnmountTargetDirectory(targetDirectory);
        List<string> volumeNames = volumes.Select(v => v.VolumeName).ToList();

        foreach (var vol in volumeNames)
        {
            var args = @$"{targetDirectory} {vol}";
            Console.WriteLine($"mountvol {args}");
            var startInfo = new ProcessStartInfo("mountvol", args)
            {
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            var process = Process.Start(startInfo);
            process.WaitForExit();
            var exitCode = process.ExitCode;
            if (exitCode == 0)
            {
                Console.WriteLine($"Successfully mapped volume to {targetDirectory}.");
                Volume = volumes.FirstOrDefault(v => v.VolumeName == vol);
                CountUnlockingAttempt = 0;
                return true;
            }
            else
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                if (output.Contains("The parameter is incorrect"))
                {
                    Console.WriteLine("Volume is not connected.");
                }
                else if (output.Contains("Access is denied"))
                {
                    throw new IOException("Unable to mount volume. Access is denied. Ensure this program is running as an Administrator.");
                }
                else
                {
                    Console.WriteLine(output);
                }
            }
        }

        return false;
    }

    private static void UnmountTargetDirectory(string targetDirectory)
    {
        var startInfo = new ProcessStartInfo("mountvol", $"\"{targetDirectory}\" /D")
        {
            RedirectStandardOutput = true,
            WindowStyle = ProcessWindowStyle.Hidden,

        };
        var process = Process.Start(startInfo);
        process.WaitForExit();
        var exitCode = process.ExitCode;
        if (exitCode != 0)
        {
            if (exitCode == 1)
            {
                Console.WriteLine("The target directory has been removed unexpectedly");
            }
            else {
                var output = process.StandardOutput.ReadToEnd().Trim();
                if (string.Equals(output, "The file or directory is not a reparse point.", StringComparison.OrdinalIgnoreCase))
                {
                    //Console.WriteLine("Directory is not a reparse point.");
                    return;
                }
                throw new IOException($"Failed to unmount target directory. Exit code: {exitCode}. {output?.Trim()}");
            }
        }
    }
}
