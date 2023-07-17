using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Sharprompt;
using ShellProgressBar;

internal class Program
{
    private static async Task Main()
    {
        try
        {
            while (true)
            {
                var bootstrapFilePath = FindBootstrapFile();
                var options = ExtractOptionsFromBootstrapFile(bootstrapFilePath);

                var selectedFile = SelectOption(options);

                var selectedFiles = PromptForFiles(selectedFile);

                using var mainProgressBar = CreateMainProgressBar(selectedFiles.Count);
                await DownloadFiles(selectedFiles, mainProgressBar);

                Console.WriteLine("Download of all episodes completed :D");
                Console.WriteLine();

                var continueChoice = Prompt.Confirm("Do you want to continue downloading files from other lists?");
                if (!continueChoice)
                    break;
            }

            Console.WriteLine("All downloads completed. Press enter to exit.");
            Console.ReadLine();
        }
        catch (Exception e)
        {
            Console.WriteLine("Something went wrong! See the error message below:");
            Console.WriteLine("Error message: " + e.Message);
            Console.ReadLine();
        }
    }

    private static string FindBootstrapFile()
    {
        while (true)
        {
            var bootstrapFile = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "__Bootstrap.txt")
                .FirstOrDefault();

            if (bootstrapFile != null)
                return bootstrapFile;

            Console.WriteLine("The __Bootstrap.txt file was not found. Please make sure it exists.");
            Console.WriteLine("Press enter to retry...");
            Console.ReadLine();
        }
    }

    private static List<FileOption> ExtractOptionsFromBootstrapFile(string filePath)
    {
        var options = new List<FileOption>();

        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines)
        {
            var content = line.Split('\t');
            if (content.Length == 2)
            {
                var url = content[0];
                var path = content[1];
                options.Add(new FileOption { Url = url, Path = path });
            }
        }

        if (options.Count == 0)
            throw new Exception("No valid options were found in the file: " + filePath);

        return options;
    }

    private static FileOption SelectOption(List<FileOption> options)
    {
        var optionTexts = options.Select(option => Path.GetFileName(option.Path)).ToArray();
        var selectedIndex = Prompt.Select("Choose a file to download:", optionTexts);
        var selectedOption = options[Array.IndexOf(optionTexts, selectedIndex)];

        if (!File.Exists(selectedOption.Path))
        {
            using var client = new HttpClient();
            var response = client.GetAsync(selectedOption.Url).Result;
            response.EnsureSuccessStatusCode();
            var content = response.Content.ReadAsByteArrayAsync().Result;
            File.WriteAllBytes(selectedOption.Path, content);
        }

        return selectedOption;
    }

    private static List<FileOption> PromptForFiles(FileOption selectedFile)
    {
        var selectedFileOptions = ExtractOptionsFromBootstrapFile(selectedFile.Path);
        var filesToDownload = selectedFileOptions.Except(new[] { selectedFile });

        var optionTexts = filesToDownload.Select(option => Path.GetFileName(option.Path)).ToArray();
        var selectedOptionTexts = Prompt.MultiSelect("Choose files to download:", optionTexts);

        var selectedFiles = filesToDownload.Where(option => selectedOptionTexts.Contains(Path.GetFileName(option.Path))).ToList();

        return selectedFiles;
    }


    private static ProgressBar CreateMainProgressBar(int totalFiles)
    {
        var mainProgressBarOptions = new ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Green,
            BackgroundColor = ConsoleColor.DarkGreen,
            ProgressCharacter = '─'
        };

        return new ProgressBar(totalFiles, "Downloading Files", mainProgressBarOptions);
    }

    private static async Task DownloadFiles(List<FileOption> options, ProgressBarBase mainProgressBar)
    {
        var tasks = options.Select(option => ProcessFileAsync(option, mainProgressBar)).ToArray();
        await Task.WhenAll(tasks);
        mainProgressBar.Tick(options.Count); // Update the main progress bar to 100%
    }

    private static async Task ProcessFileAsync(FileOption option, ProgressBarBase mainProgressBar)
    {
        var childProgressBarOptions = new ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Yellow,
            BackgroundColor = ConsoleColor.DarkYellow,
            ProgressCharacter = '─'
        };

        var tempFilePath = Path.GetTempFileName();

        if (!File.Exists(option.Path))
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(option.Url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsByteArrayAsync();
            File.WriteAllBytes(tempFilePath, content);
        }

        var lines = await File.ReadAllLinesAsync(tempFilePath);
        File.Delete(tempFilePath);

        using var childProgressBar = mainProgressBar.Spawn(lines.Length, "Downloading: " + Path.GetFileName(option.Path),
            childProgressBarOptions);

        foreach (var line in lines)
        {
            var content = line.Split('\t');
            var url = content[0];
            var path = content[1];

            if (File.Exists(path))
            {
                childProgressBar.Tick();
                continue;
            }

            var directoryName = Path.GetDirectoryName(path);

            if (directoryName != null)
            {
                try
                {
                    using var client2 = new HttpClient();
                    using var response2 = await client2.GetAsync(url);
                    response2.EnsureSuccessStatusCode();
                    var contentStream2 = await response2.Content.ReadAsStreamAsync();

                    var tempfile2 = Path.GetTempFileName();
                    await using (var fs2 = new FileStream(tempfile2, FileMode.OpenOrCreate))
                    {
                        await contentStream2.CopyToAsync(fs2);
                    }

                    Directory.CreateDirectory(directoryName);
                    File.Move(tempfile2, path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Unable to download file from {url}. Error: {ex.Message}. Skipping download.");
                }
            }

            childProgressBar.Tick();
        }

        mainProgressBar.Tick(); // Update the main progress bar after all child progress is complete
    }
}

internal class FileOption
{
    public string Url { get; set; }
    public string Path { get; set; }
}
