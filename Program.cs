using Sharprompt;
using ShellProgressBar;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Represents a file option with URL and path information.
/// </summary>
internal class FileOption
{
    /// <summary>
    /// Gets or sets the URL of the file to be downloaded.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the local path where the file will be saved.
    /// </summary>
    public string? Path { get; set; }
}

/// <summary>
/// Represents the main program class.
/// </summary>
internal class Program
{
    private static readonly string BootstrapUrl = "https://denkortewiki.dk/lister/__Bootstrap.txt";

    /// <summary>
    /// The main entry point of the program.
    /// </summary>
    private static async Task Main()
    {
        try
        {
            while (true)
            {
                var bootstrapOptions = await FetchBootstrapOptions();
                var selectedOption = SelectBootstrapOption(bootstrapOptions);

                var availableFiles = ProcessBootstrapContent(selectedOption);

                var pageSize = GetOptimalPageSize();
                var selectedFiles = PromptForFiles(availableFiles, pageSize);

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

    /// <summary>
    /// Fetches the bootstrap options from the remote server.
    /// </summary>
    /// <returns>A list of bootstrap file options.</returns>
    private static async Task<List<FileOption>> FetchBootstrapOptions()
    {
        var options = new List<FileOption>();

        using var client = new HttpClient();
        var response = await client.GetAsync(BootstrapUrl);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length == 2)
            {
                var url = parts[0];
                var path = parts[1];
                options.Add(new FileOption { Url = url, Path = path });
            }
        }

        if (options.Count == 0)
            throw new Exception("No valid options were found in the file: " + BootstrapUrl);

        return options;
    }

    /// <summary>
    /// Selects a bootstrap option from the available options.
    /// </summary>
    /// <param name="bootstrapOptions">The list of available bootstrap options.</param>
    /// <returns>The selected bootstrap file option.</returns>
    private static FileOption SelectBootstrapOption(List<FileOption> bootstrapOptions)
    {
        var optionTexts = bootstrapOptions.Select(option => Path.GetFileName(option.Url)).ToArray();
        var selectedIndex = Prompt.Select("Choose a file to download", optionTexts);
        var selectedOption = bootstrapOptions[Array.IndexOf(optionTexts, selectedIndex)];

        return selectedOption;
    }

    /// <summary>
    /// Processes the content of the selected bootstrap option and returns a list of file options.
    /// </summary>
    /// <param name="selectedBootstrapOption">The selected bootstrap option.</param>
    /// <returns>A list of file options.</returns>
    private static List<FileOption> ProcessBootstrapContent(FileOption selectedBootstrapOption)
    {
        if (selectedBootstrapOption.Url == null)
            return new List<FileOption>();

        using var client = new HttpClient();
        var response = client.GetAsync(selectedBootstrapOption.Url).Result;
        response.EnsureSuccessStatusCode();
        var content = response.Content.ReadAsStringAsync().Result;

        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        return lines.Select(line =>
        {
            var parts = line.Split('\t');
            if (parts.Length == 2)
            {
                var url = parts[0];
                var path = parts[1];
                return new FileOption { Url = url, Path = path };
            }
            return null;
        })
        .Where(option => option != null)
        .ToList();
    }

    /// <summary>
    /// Prompts the user to choose files to download from the available files.
    /// </summary>
    /// <param name="availableFiles">The list of available files.</param>
    /// <param name="pageSize">The page size for pagination.</param>
    /// <returns>The list of selected files.</returns>
    private static List<FileOption> PromptForFiles(List<FileOption> availableFiles, int pageSize)
    {
        var optionTexts = availableFiles.Select(option => Path.GetFileName(option.Url)).ToArray();
        var selectedOptionTexts =
            Prompt.MultiSelect("Choose files to download, use left/right arrow keys for pagination", optionTexts,
                pageSize);

        var selectedFiles = availableFiles
            .Where(option => selectedOptionTexts.Contains(Path.GetFileName(option.Url)))
            .ToList();

        return selectedFiles;
    }

    /// <summary>
    /// Creates the main progress bar.
    /// </summary>
    /// <param name="totalFiles">The total number of files to be downloaded.</param>
    /// <returns>The main progress bar.</returns>
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

    /// <summary>
    /// Downloads the selected files and updates the progress bar.
    /// </summary>
    /// <param name="options">The list of selected file options.</param>
    /// <param name="mainProgressBar">The main progress bar.</param>
    /// <returns>The task representing the asynchronous operation.</returns>
    private static async Task DownloadFiles(List<FileOption> options, ProgressBarBase mainProgressBar)
    {
        var tasks = options.Select(option => ProcessFileAsync(option, mainProgressBar)).ToArray();
        await Task.WhenAll(tasks);
        mainProgressBar.Tick(options.Count); // Update the main progress bar to 100%
    }

    /// <summary>
    /// Processes a single file asynchronously and updates the progress bar.
    /// </summary>
    /// <param name="option">The file option to be processed.</param>
    /// <param name="mainProgressBar">The main progress bar.</param>
    /// <returns>The task representing the asynchronous operation.</returns>
    private static async Task ProcessFileAsync(FileOption option, ProgressBarBase mainProgressBar)
    {
        var childProgressBarOptions = new ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Yellow,
            BackgroundColor = ConsoleColor.DarkYellow,
            ProgressCharacter = '─'
        };

        var tempFilePath = Path.GetTempFileName();

        using var client = new HttpClient();
        using var response = await client.GetAsync(option.Url);
        response.EnsureSuccessStatusCode();
        await using var contentStream = await response.Content.ReadAsStreamAsync();

        await using (var fs = new FileStream(tempFilePath, FileMode.Create))
        {
            await contentStream.CopyToAsync(fs);
        }

        var lines = await File.ReadAllLinesAsync(tempFilePath);
        File.Delete(tempFilePath);

        using var childProgressBar = mainProgressBar.Spawn(lines.Length,
            "Downloading: " + Path.GetFileName(option.Path),
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
                try
                {
                    using var client2 = new HttpClient();
                    using var response2 = await client2.GetAsync(url);
                    response2.EnsureSuccessStatusCode();
                    var contentStream2 = await response2.Content.ReadAsStreamAsync();

                    var tempfile = Path.GetTempFileName();
                    await using (var fs2 = new FileStream(tempfile, FileMode.Create))
                    {
                        await contentStream2.CopyToAsync(fs2);
                    }

                    Directory.CreateDirectory(directoryName);
                    File.Move(tempfile, path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"WARNING: Unable to download file from {url}. Error: {ex.Message}. Skipping download.");
                }

            childProgressBar.Tick();
        }

        mainProgressBar.Tick(); // Update the main progress bar after all child progress is complete
    }

    /// <summary>
    /// Gets the optimal page size for pagination.
    /// </summary>
    /// <returns>The optimal page size.</returns>
    private static int GetOptimalPageSize()
    {
        var windowHeight = Console.WindowHeight;
        var pageSize = windowHeight - 5;

        return pageSize > 0 ? pageSize : 1;
    }
}
