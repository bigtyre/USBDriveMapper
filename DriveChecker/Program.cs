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
using System.Text.RegularExpressions;

namespace DriveChecker;

internal class Program
{
    public static Volume Volume { get; set; }
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
            Console.WriteLine("Warning: This app should be run as Administrator or a system account.");
            //return;
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

            var delay = checkInterval;

            try
            {
                bool isEmpty = false;
                do
                {
                    //Console.WriteLine($"Trying to get files in {targetDirectory}");
                    var paths = Directory.EnumerateFileSystemEntries(targetDirectory);
                    isEmpty = !paths.Any();

                    if (!isEmpty)
                    {
                    }
                    else
                    {
                        Console.WriteLine("Path is Empty! Alert!");
                        try
                        {
                            bool anyDriveMapped = RemapVolumes();
                            if (!anyDriveMapped)
                            {
                                await Task.Delay(500);
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
                HandleIOException(Volume, ex.Message);
                delay = TimeSpan.FromSeconds(1);
            }
            catch (Exception ex) {
                Console.WriteLine(ex.Message);
            }

            await Task.Delay(delay);
        }
    }

    private static void HandleIOException(Volume volume, string message)
    {
        bool isBitLockerException = message.Contains("This drive is locked by BitLocker Drive Encryption", StringComparison.OrdinalIgnoreCase);
        if (isBitLockerException is false)
        {
            Console.WriteLine(message);
            return;
        }


        Console.WriteLine("This drive/directory is locked by BitLocker Drive Encryption");
        string volumeDir;
        if (IsTargetADirectory(message))
        {
            volumeDir = GetTargetDriveFromMessage(message, "'", "'");
        }
        else
        {
            volumeDir = GetTargetDriveFromMessage(message, "'", ":") + ":";
        }

        Console.WriteLine(volumeDir);

        if (volumeDir != null)
        {
            Console.WriteLine($"Attempting to unlock BitLocker volume: '{volumeDir}'.");
            DecryptBitLockerEncryption(volume, $"{volumeDir}");
        }
        else
        {
            Console.WriteLine("Failed to map the target directory. Remapping");
        }
    }

    private static bool IsTargetADirectory(string message)
    {
        bool isNtfs = false;
        string ntfsVolumeDrive = GetTargetDriveFromMessage(message, "'", "'");
            if(ntfsVolumeDrive != null && ntfsVolumeDrive.Length > 4) isNtfs = true;
        return isNtfs;
    }

    private static string GetTargetDriveFromMessage(string message, string start, string end)
    {
        if (!message.Contains(start) || !message.Contains(end))
        {
            return string.Empty;
        }

        int Start, End;
        Start = message.IndexOf(start, 0) + start.Length;
        End = message.IndexOf(end, Start);
        return message.Substring(Start, End - Start);
    }
    /*
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
                 *

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
    */
    private static void DecryptBitLockerEncryption(Volume volume, string targetDir)
    { 
        try {
            Console.WriteLine($"{nameof(DecryptBitLockerEncryption)} started");

            string password = volume.BitLockerPassword;
            if(string.IsNullOrEmpty(password)) { 
                Console.WriteLine("Password is empty in the App Settings. Please update and restart the service."); 
                return; 
            }
        
            Console.WriteLine($"Attempting to unlock {volume.Title}, target dir: {targetDir}");
            if(CountUnlockingAttempt >= UnlockingAttempts)
            {
                Console.WriteLine($"Attempt {CountUnlockingAttempt}: Failed to unlock the driver due to wrong credentials. Please update the App Settings");
            }

            var escapedPassword = EscapeForPowerShell(password);

            string secureString = $"$SecureString = ConvertTo-SecureString '{escapedPassword}' -AsPlainText -Force";
            string mountPoint = $"Unlock-BitLocker -MountPoint \"{targetDir}\" -Password $SecureString";
            string unlockBitLocker = secureString + "; " + mountPoint;


            var startInfo = new ProcessStartInfo
            {
              FileName = "powershell.exe",
              Arguments = unlockBitLocker,
              //UseShellExecute = true,
              Verb = "runas",
              CreateNoWindow = true,
              WindowStyle = ProcessWindowStyle.Hidden,
              RedirectStandardOutput = true
            };

            var process = Process.Start(startInfo);
            process.WaitForExit();

            var output = process.StandardOutput.ReadToEnd();
            Console.WriteLine(output);

            CountUnlockingAttempt++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Encountered an error in {nameof(DecryptBitLockerEncryption)}: {ex.Message}");
            throw;
        }
        finally
        {
            Console.WriteLine($"{nameof(DecryptBitLockerEncryption)} finished");
        }
    }

    private static readonly Regex CharactersToEscape = new(@"['""$]"); // Extend the character set as required

    public static string EscapeForPowerShell(string input)
    {
        // $& is the characters that were matched
        return CharactersToEscape.Replace(input, "`$&");
    }
    private static AppSettings GetSettings(string[] args)
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddJsonFile("appSettings.json", optional: true);
        configBuilder.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appSettings.json"), optional: true);

        var programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var programDataSettingsFilePath = Path.Combine(programDataPath, @"Big Tyre\Backup HDD Attacher\appSettings.json");

        configBuilder.AddJsonFile(programDataSettingsFilePath, optional: true);
        configBuilder.AddCommandLine(args);
#if DEBUG
        configBuilder.AddUserSecrets<Program>();
#endif
        var config = configBuilder.Build();

        var settings = new AppSettings();
        config.Bind(settings);

        return settings;
    }

    static bool TryRemapVolumes(string targetDirectory, List<Volume> volumes)
    {
        Console.WriteLine($"Attempting to remap volumes.");

        UnmountTargetDirectory(targetDirectory);

        foreach (var volume in volumes)
        {
            var volumeLabel = volume.Title;
            var volumeName = volume.VolumeName;
            Console.WriteLine($"Attempting to mount volume {volumeLabel}");

            var args = @$"{targetDirectory} {volumeName}";

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
                Volume = volume;
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
