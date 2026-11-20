namespace MonoInterop;

public static class SpecialWords
{
    public static void Parse(string Path)
    {
        if (!File.Exists(Path))
        {
            if (Directory.Exists(Path))
            {
                foreach (var File in Directory.EnumerateFiles(Path))
                {
                    Parse(File);
                }
                foreach (var dir in Directory.EnumerateDirectories(Path))
                {
                    Parse(dir);
                }
                return;
            }
            throw new Exception("the keywords path was specified but it isn't present!");
        }
        foreach (var Line in File.ReadAllLines(Path))
        {
            if (Line.StartsWith("#define "))
            {
                string[] parts = Line.Split(' ');
                Constants.SpecialNames.Add(parts[1]);
            }
        }
    }
}