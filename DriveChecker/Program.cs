using BigTyre.DiskMount.Configuration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriveChecker;


internal partial class Program
{
    //[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    //static extern bool SetVolumeMountPoint(string mountPoint, string volumeName);

    //[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    //static extern bool DeleteVolumeMountPoint(string mountPoint);



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
            if (Volume == null) 
                RemapVolumes();

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
            catch (DirectoryNotFoundException)
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
        return message[Start..End];
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
    /*
    static bool TryRemapVolumes(string targetDrive, List<Volume> volumes)
    {
        Console.WriteLine("Attempting to remap volumes.");

        foreach (var volume in volumes)
        {
            Console.WriteLine($"Attempting to mount volume {volume.Title}");

            // remove existing mount points
            Run("mountvol", $"{volume.VolumeName} /P");

            // mount to desired drive
            Run("mountvol", $"{targetDrive} {volume.VolumeName}");

            if (Directory.Exists(targetDrive))
            {
                Console.WriteLine($"Successfully mapped volume to {targetDrive}");
                Volume = volume;
                CountUnlockingAttempt = 0;
                return true;
            }
        }

        return false;
    }*/
    /*
    static void Run(string exe, string args)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
            RedirectStandardOutput = true
        });

        p.WaitForExit();
    }*/

    static void RemoveAllMountPoints(string volumeGuid)
    {
        Console.WriteLine($"Listing mount points for {volumeGuid}");

        var output = Run("mountvol", "");

        if (string.IsNullOrWhiteSpace(output))
            return;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        bool inTargetVolume = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Detect volume header
            if (trimmed.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
            {
                inTargetVolume = trimmed.Equals(volumeGuid, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inTargetVolume)
                continue;

            if (trimmed.StartsWith("***"))
                continue;

            Console.WriteLine($"Removing mount point: {trimmed}");

            Run("mountvol", $"{trimmed} /D");
        }
    }
    /*
    static void RemoveAllMountPointsOld(string volumeGuid)
    {
        Console.WriteLine($"Listing mount points for {volumeGuid}");

        var output = Run("mountvol", $"{volumeGuid} /L");

        if (string.IsNullOrWhiteSpace(output))
        {
            Console.WriteLine("No mount points found.");
            return;
        }

        var lines = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim());

        foreach (var line in lines)
        {
            // We only care about drive-letter mount points (e.g. D:\)
            if (line.EndsWith(":\\", StringComparison.OrdinalIgnoreCase))
            {
                var drive = line.TrimEnd('\\');

                Console.WriteLine($"Removing mount point {drive}");
                Run("mountvol", $"{drive} /D");
            }
            else
            {
                // If you ever reintroduce folder mounts, handle them here
                Console.WriteLine($"Skipping non-drive mount point: {line}");
            }
        }
    }
    */
    static HashSet<string> GetConnectedVolumes()
    {
        var output = Run("mountvol", "");

        var volumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(output))
            return volumes;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
            {
                volumes.Add(trimmed);
            }
        }

        return volumes;
    }


    static bool TryRemapVolumes(string targetDrive, List<Volume> volumes)
    {
        Console.WriteLine("Attempting to remap volumes.");
        Console.WriteLine($"Target drive: {targetDrive}");

        //string driveLetter = targetDrive.TrimEnd('\\');

        var connectedVolumes = GetConnectedVolumes();
        Console.WriteLine($"-------------------------------");
        Console.WriteLine($"Connected volumes: " + connectedVolumes.Count);
        foreach (var v in connectedVolumes)
        {
            Console.WriteLine(v);
        }
        Console.WriteLine($"-------------------------------");


        foreach (var volume in volumes)
        {
            var volumeName = volume.Title;
            var volumeGuid = volume.VolumeName.Trim();

            Console.WriteLine("---------------------------------------");
            Console.WriteLine($"Trying volume: {volumeName}");
            Console.WriteLine($"Volume GUID: {volumeGuid}");

            if (!connectedVolumes.Contains(volumeGuid, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Volume {volumeName} is not connected. Skipping.");
                continue;
            }

            RemoveAllMountPoints(volumeGuid);

            // Determine mount path
            string mountPath = targetDrive;
            //string unmountPath;

            if (targetDrive.Length == 2 && targetDrive[1] == ':')
            {
                // Drive letter form
                mountPath = targetDrive + "\\";
                //unmountPath = targetDrive;
            }

            Console.WriteLine($"Mounting {volumeGuid} to {mountPath}");
            Run("mountvol", $"{mountPath} {volumeGuid}");

            bool exists = Directory.Exists(mountPath);
            Console.WriteLine($"Directory.Exists({mountPath}) = {exists}");

            // To to enumerate files in the directory, so we can unlock it if it's locked by BitLocker. This is required because sometimes the drive is not accessible immediately after mounting, even though Directory.Exists returns true.
            try
            {
                bool isEmpty = false;
                do
                {
                    //Console.WriteLine($"Trying to get files in {targetDirectory}");
                    var paths = Directory.EnumerateFileSystemEntries(mountPath);
                    isEmpty = !paths.Any();
                } while (isEmpty);
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine($"Directory '{mountPath}' not found.");
            }
            catch (IOException ex)
            {
                HandleIOException(volume, ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }



            if (exists)
            {
                Console.WriteLine($"SUCCESS: {volumeName} mounted at {mountPath}");
                Volume = volume;
                CountUnlockingAttempt = 0;
                return true;
            }

            Console.WriteLine($"Mount attempt failed for {volume.Title}");
        }

        Console.WriteLine("No volumes could be mounted.");
        return false;
    }

    static bool IsVolumeConnected(string volumeGuid)
    {
        var output = Run("mountvol", "");

        if (string.IsNullOrWhiteSpace(output))
            return false;

        return output.Contains(volumeGuid, StringComparison.OrdinalIgnoreCase);
    }

    static string Run(string exe, string args)
    {
        Console.WriteLine($"> {exe} {args}");

        var startInfo = new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var p = Process.Start(startInfo);

        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();

        p.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stderr))
            Console.WriteLine("stderr: " + stderr.Trim());

        if (!string.IsNullOrWhiteSpace(stdout))
            Console.WriteLine("stdout: " + stdout.Trim());

        Console.WriteLine($"exit code: {p.ExitCode}");

        return stdout;
    }

    private static void DecryptBitLockerEncryption(Volume volume, string targetDir)
    { 
        var functionName = nameof(DecryptBitLockerEncryption);
        try {
            Console.WriteLine($"{functionName} started");

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
            Console.WriteLine($"Encountered an error in {functionName}: {ex.Message}");
            throw;
        }
        finally
        {
            Console.WriteLine($"{functionName} finished");
        }
    }

    private static readonly Regex CharactersToEscape = MyRegex(); // Extend the character set as required

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
    /*
    static bool TryRemapVolumes(string targetDirectory, List<Volume> volumes)
    {
        Console.WriteLine("Attempting to remap volumes.");

        var mountPoint = EnsureTrailingSlash(targetDirectory);

        UnmountTargetDirectory(mountPoint);

        foreach (var volume in volumes)
        {
            Console.WriteLine($"Attempting to mount volume {volume.Title}");

            var volumeName = EnsureTrailingSlash(volume.VolumeName);

            bool result = SetVolumeMountPoint(mountPoint, volumeName);

            if (result)
            {
                Console.WriteLine($"Successfully mapped volume to {mountPoint}");
                Volume = volume;
                CountUnlockingAttempt = 0;
                return true;
            }

            var error = Marshal.GetLastWin32Error();

            switch (error)
            {
                case 2: // ERROR_FILE_NOT_FOUND
                case 3: // ERROR_PATH_NOT_FOUND
                    Console.WriteLine("Volume is not connected.");
                    break;

                case 5: // ERROR_ACCESS_DENIED
                    throw new IOException("Unable to mount volume. Access denied. Ensure the program runs as Administrator.");

                default:
                    Console.WriteLine($"Mount failed with Win32 error {error}");
                    break;
            }
        }

        return false;
    }
    */
    /*

    static string EnsureTrailingSlash(string path)
    {
        if (!path.EndsWith('\\'))
            return path + "\\";
        return path;
    }
    private static void UnmountTargetDirectory(string targetDirectory)
    {
        Console.WriteLine("Attempting to unmount directory: " + targetDirectory);

        var mountPoint = EnsureTrailingSlash(targetDirectory);

        bool result = DeleteVolumeMountPoint(mountPoint);

        if (result)
            return;

        var error = Marshal.GetLastWin32Error();

        if (error == 2 || error == 3 || error == 4390 || error == 5)
        {
            Console.WriteLine("Mount point not present.");
            return;
        }

        throw new IOException($"Failed to unmount target directory. Win32 error {error}");
    }
    static bool TryRemapVolumesOld(string targetDirectory, List<Volume> volumes)
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

    private static void UnmountTargetDirectoryOld(string targetDirectory)
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
    */
    [GeneratedRegex(@"['""$]")]
    private static partial Regex MyRegex();
}
