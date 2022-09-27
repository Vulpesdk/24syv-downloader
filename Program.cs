using Sharprompt;
using ShellProgressBar;

internal class Program
{
    private static void Main(string[] args)
    {
        string url = "Udefineret";
        string path = "Udefineret";

        try
        {

            string[] txtFiles = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.txt").Select(x => Path.GetFileName(x)).ToArray();

            if (txtFiles.Length == 0)
            {
                throw new Exception("Placer venligst minimum 1 dataliste (txt-fil) ved siden af exe-filen");
            }

            string[] selectedFiles = Prompt.MultiSelect("Vælg relevante tekstfiler vha. piletast op/ned, marker filer med mellemrum og tryk enter\n", txtFiles).ToArray();

            foreach (var file in selectedFiles)
            {
                string[] lines = File.ReadAllLines(file);

                var options = new ProgressBarOptions
                {
                    ProgressCharacter = '─',
                    ProgressBarOnBottom = true
                };
                using (var pbar = new ProgressBar(lines.Length, "Downloader: " + file, options))
                {
                    using var client = new HttpClient();

                    foreach (var line in lines)
                    {
                        string[] content = line.Split('\t');

                        url = content[0];
                        path = content[1];

                        if (File.Exists(path))
                        {
                            pbar.Tick();
                            continue;
                        }

                        string? directoryName = Path.GetDirectoryName(path);

                        if (directoryName != null)
                        {
                            using (var s = client.GetStreamAsync(url))
                            {

                                string tempfile = Path.GetTempFileName();
                                using var fs = new FileStream(tempfile, FileMode.OpenOrCreate);

                                s.Result.CopyTo(fs);

                                fs.Close();

                                Directory.CreateDirectory(directoryName);

                                File.Move(tempfile, path);
                            }
                            pbar.Tick();
                        }
                    }
                }
            }

            while (true)
            {
                Console.Write("Download af alle episoder er fuldført :D (tryk på enter for at lukke)\n");

                ConsoleKeyInfo keyPress = Console.ReadKey(intercept: true);
                while (keyPress.Key == ConsoleKey.Enter)
                {
                    Environment.Exit(0);
                }
            }
        }
        catch (Exception e)
        {
            while (true)
            {
                Console.Write("Noget gik galt! Se fejlen nedenfor (tryk på enter for at lukke)\n");
                Console.Write("Download url: " + url + "\n");
                Console.Write("Fil path: " + path + "\n");
                Console.Write("Genereret fejlbesked: " + e.Message);

                ConsoleKeyInfo keyPress = Console.ReadKey(intercept: true);
                while (keyPress.Key == ConsoleKey.Enter)
                {
                    Environment.Exit(0);
                }
            }
        }
    }
}

