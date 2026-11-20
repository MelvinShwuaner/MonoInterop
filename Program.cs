using System.Text;
using CommandLine;
using MonoInterop;
public class Options
{
    [Option('i', "input", Required = true, HelpText = "Input folder/file for managed dlls")]
    public string Input { get; set; }
    [Option('o', "output", Required = true, HelpText = "output folder for managed dlls")]
    public string OutPut { get; set; }
    [Option('c', "include-c", Required = false, HelpText = "include compiler generated types", Default = false)]
    public bool IncludeCompiledTypes { get; set; }
    [Option('p', "include-p", Required = false, HelpText = "include primitive types", Default = false)]
    public bool IncludePrimitives { get; set; }
    [Option('x', "ignore-e", Required = false, HelpText = "ignore exceptions in parsing dlls", Default = false)]
    public bool IgnoreExceptions { get; set; }
    [Option('k', "keyword-path", Required = false, HelpText = "special keywords which your mono dlls must not conflict with. accepts #define key", Default = null)]
    public string? KeyWords { get; set; }
    [Option('r', "root-asm", Required = false, HelpText = "root assemblies: to reduce file size, you can have the generator strip any classes which are not included / referenced by these assemblies", Default = null)]
    public string? RootAssemblies { get; set; }
    public static Options GlobalOptions;
}
class Program
{


    static string GetQualifiedPath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }
        return Path.Combine(Environment.CurrentDirectory, path);
    }
    static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(o =>
            {
                Options.GlobalOptions = o;
                DirectoryInfo input = new DirectoryInfo(GetQualifiedPath(o.Input));
                if (o.KeyWords != null)
                {
                    SpecialWords.Parse(GetQualifiedPath(o.KeyWords));
                }

                if (!input.Exists)
                {
                    throw new ArgumentException("Input folder doesn't exist!");
                }

                DirectoryInfo output = new DirectoryInfo(GetQualifiedPath(o.OutPut));
                if (output.Exists)
                {
                    Directory.Delete(output.FullName, true);
                }

                Directory.CreateDirectory(output.FullName);
                Console.WriteLine("Processing managed dlls!");
                /*foreach (var info in DLLReader.ReadDLLs(input))
                {
                    File.WriteAllText(Path.Combine(output.FullName, info.name), info.File);
                }*/
                DLLInfo Main = DLLReader.ReadDLLsAsOne(input);
                File.WriteAllText(Path.Combine(output.FullName, Main.name), Main.File);
            }).WithNotParsed(errors =>
            {
                StringBuilder error = new  StringBuilder();
                foreach (var err in errors)
                {
                    error.AppendLine(err.ToString());
                }
                throw new ArgumentException($"failed to parse arguments! {error}");
            });
    }
}