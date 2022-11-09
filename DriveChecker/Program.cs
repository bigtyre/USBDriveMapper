using BigTyre.DiskMount.Configuration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace DriveChecker;

internal class Program
{
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
        var volumeNames = settings.VolumeNames;
        var checkInterval = TimeSpan.FromSeconds(settings.CheckFrequencyInSeconds);

        void RemapVolumes() => TryRemapVolumes(targetDirectory, volumeNames);

        while (true)
        {
            try
            {
                bool isEmpty = false;
                do
                {
                    var paths = Directory.EnumerateFileSystemEntries(targetDirectory);
                    isEmpty = !paths.Any();

                    if (isEmpty)
                    {
                        Console.WriteLine("Path is Empty! Alert!");
                        try
                        {
                            RemapVolumes();
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
                Console.WriteLine(ex.Message);
            }

            await Task.Delay(checkInterval);
        }
    }

    private static AppSettings GetSettings(string[] args)
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddJsonFile("appSettings.json", optional: true);
        configBuilder.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appSettings.json"), optional: true);
        configBuilder.AddCommandLine(args);
        var config = configBuilder.Build();

        var settings = new AppSettings();
        config.Bind(settings);

        return settings;
    }

    static void TryRemapVolumes(string targetDirectory, List<string> volumeNames)
    {
        Console.WriteLine($"Attempting to remap volumes.");

        UnmountTargetDirectory(targetDirectory);

        foreach (var vol in volumeNames)
        {
            var args = @$"{targetDirectory} {vol}";
            Console.WriteLine($"mountvol {args}");
            var startInfo = new ProcessStartInfo("mountvol", args)
            {
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            var process = Process.Start(startInfo);
            process.WaitForExit();
            var exitCode = process.ExitCode;
            if (exitCode == 0)
            {
                Console.WriteLine($"Successfully mapped volume to {targetDirectory}.");
                return;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            Console.WriteLine(output);
        }
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
