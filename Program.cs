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

                var pageSize = GetOptimalPageSize();
                var selectedFiles = PromptForFiles(selectedFile, pageSize);

                using var mainProgressBar = CreateMainProgressBar(selectedFiles.Count);
                await DownloadFiles(selectedFiles, mainProgressBar);

                RemoveFile(selectedFile); // Remove the selected file after download

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
    ///     Finds the __Bootstrap.txt file in the current directory.
    /// </summary>
    /// <returns>The path of the bootstrap file.</returns>
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

    /// <summary>
    ///     Extracts FileOption list from the given file.
    /// </summary>
    /// <param name="filePath">The path of the bootstrap file.</param>
    /// <returns>List of FileOptions.</returns>
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

    /// <summary>
    ///     Selects a FileOption from the provided list of options.
    /// </summary>
    /// <param name="options">List of FileOptions to choose from.</param>
    /// <returns>The selected FileOption.</returns>
    private static FileOption SelectOption(List<FileOption> options)
    {
        var optionTexts = options.Select(option => Path.GetFileName(option.Path)).ToArray();
        var selectedIndex = Prompt.Select("Choose a file to download", optionTexts);
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

    /// <summary>
    ///     Prompts the user to select files for downloading from the provided FileOption list.
    /// </summary>
    /// <param name="selectedFile">The initially selected FileOption.</param>
    /// <param name="pageSize">The optimal page size for displaying prompts.</param>
    /// <returns>List of selected FileOptions.</returns>
    private static List<FileOption> PromptForFiles(FileOption selectedFile, int pageSize)
    {
        var selectedFileOptions = ExtractOptionsFromBootstrapFile(selectedFile.Path);
        var filesToDownload = selectedFileOptions.Except(new[] { selectedFile });

        var optionTexts = filesToDownload.Select(option => Path.GetFileName(option.Path)).ToArray();
        var selectedOptionTexts =
            Prompt.MultiSelect("Choose files to download, use left/right arrow keys for pagination", optionTexts,
                pageSize);

        var selectedFiles = filesToDownload
            .Where(option => selectedOptionTexts.Contains(Path.GetFileName(option.Path)))
            .ToList();

        return selectedFiles;
    }

    /// <summary>
    ///     Creates a progress bar for the main download process.
    /// </summary>
    /// <param name="totalFiles">The total number of files to download.</param>
    /// <returns>A new ProgressBar instance.</returns>
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
    ///     Downloads the files from the provided list of options.
    /// </summary>
    /// <param name="options">List of FileOptions to download.</param>
    /// <param name="mainProgressBar">The main progress bar.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    private static async Task DownloadFiles(List<FileOption> options, ProgressBarBase mainProgressBar)
    {
        var tasks = options.Select(option => ProcessFileAsync(option, mainProgressBar)).ToArray();
        await Task.WhenAll(tasks);
        mainProgressBar.Tick(options.Count); // Update the main progress bar to 100%
    }

    /// <summary>
    ///     Processes a file asynchronously, downloading its content and updating the progress.
    /// </summary>
    /// <param name="option">The FileOption to process.</param>
    /// <param name="mainProgressBar">The main progress bar.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
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
                    using var client = new HttpClient();
                    using var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var contentStream = await response.Content.ReadAsStreamAsync();

                    var tempfile = Path.GetTempFileName();
                    await using (var fs = new FileStream(tempfile, FileMode.OpenOrCreate))
                    {
                        await contentStream.CopyToAsync(fs);
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
    ///     Removes the file represented by the given FileOption.
    /// </summary>
    /// <param name="file">The FileOption representing the file to be removed.</param>
    private static void RemoveFile(FileOption file)
    {
        if (File.Exists(file.Path)) File.Delete(file.Path);
    }

    /// <summary>
    ///     Calculates the optimal page size for displaying prompts.
    /// </summary>
    /// <returns>The optimal page size.</returns>
    private static int GetOptimalPageSize()
    {
        var windowHeight = Console.WindowHeight;
        var pageSize = windowHeight - 5;

        return pageSize > 0 ? pageSize : 1;
    }
}

internal class FileOption
{
    public string Url { get; set; }
    public string Path { get; set; }
}