using Sharprompt;
using ShellProgressBar;

internal class Program
{
    /// <summary>
    ///     Entry point of the application.
    /// </summary>
    private static async Task Main()
    {
        try
        {
            var txtFiles = GetTextFiles();

            var pageSize = GetOptimalPageSize();
            var selectedFiles = SelectFiles(txtFiles, pageSize);

            using var mainProgressBar = CreateMainProgressBar(selectedFiles.Length);
            await DownloadFiles(selectedFiles, mainProgressBar);

            Console.WriteLine("Download af alle episoder er fuldført :D (tryk på enter for at lukke)");
            Console.ReadLine();
        }
        catch (Exception e)
        {
            Console.WriteLine("Noget gik galt! Se fejlen nedenfor");
            Console.WriteLine("Genereret fejlbesked: " + e.Message);
            Console.ReadLine();
        }
    }

    /// <summary>
    ///     Retrieves the list of text files in the current directory.
    /// </summary>
    /// <returns>The array of text file paths.</returns>
    private static IEnumerable<string> GetTextFiles()
    {
        var txtFiles = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.txt")
            .Select(Path.GetFileName).ToArray();

        if (txtFiles.Length == 0)
            throw new Exception("Placer venligst minimum 1 dataliste (txt-fil) ved siden af exe-filen");

        return txtFiles!;
    }

    /// <summary>
    ///     Prompts the user to select files from the provided list.
    /// </summary>
    /// <param name="txtFiles">The array of text file paths.</param>
    /// <param name="pageSize">The page size for displaying prompts.</param>
    /// <returns>The array of selected file paths.</returns>
    private static string[] SelectFiles(IEnumerable<string> txtFiles, int pageSize)
    {
        return Prompt.MultiSelect(
            "Brug venstre/højre pil for at navigere sider. Marker med mellemrum og tryk enter for at bekræfte",
            txtFiles,
            pageSize).ToArray();
    }

    /// <summary>
    ///     Creates the main progress bar for the file download process.
    /// </summary>
    /// <param name="totalFiles">The total number of files to download.</param>
    /// <returns>The created main progress bar.</returns>
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
    ///     Downloads the selected files and updates the main progress bar.
    /// </summary>
    /// <param name="selectedFiles">The array of selected file paths.</param>
    /// <param name="mainProgressBar">The main progress bar.</param>
    /// <returns>The task representing the download process.</returns>
    private static async Task DownloadFiles(IReadOnlyCollection<string> selectedFiles, ProgressBarBase mainProgressBar)
    {
        var tasks = selectedFiles.Select(file => ProcessFileAsync(file, mainProgressBar)).ToArray();
        await Task.WhenAll(tasks);
        mainProgressBar.Tick(selectedFiles.Count); // Update the main progress bar to 100%
    }

    /// <summary>
    ///     Processes the specified file asynchronously and updates the child progress bar.
    /// </summary>
    /// <param name="file">The file to process.</param>
    /// <param name="mainProgressBar">The main progress bar.</param>
    private static async Task ProcessFileAsync(string? file, ProgressBarBase mainProgressBar)
    {
        var lines = await File.ReadAllLinesAsync(file);
        var childProgressBarOptions = new ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Yellow,
            BackgroundColor = ConsoleColor.DarkYellow,
            ProgressCharacter = '─'
        };

        using var childProgressBar = mainProgressBar.Spawn(lines.Length, "Downloading: " + Path.GetFileName(file),
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
                using var client = new HttpClient();
                await using var s = await client.GetStreamAsync(url);
                var tempfile = Path.GetTempFileName();
                await using (var fs = new FileStream(tempfile, FileMode.OpenOrCreate))
                {
                    await s.CopyToAsync(fs);
                }

                Directory.CreateDirectory(directoryName);
                File.Move(tempfile, path);
            }

            childProgressBar.Tick();
        }

        mainProgressBar.Tick(); // Update the main progress bar after all child progress is complete
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