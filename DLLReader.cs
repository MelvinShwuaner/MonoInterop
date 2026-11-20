using System.Text;
using Mono.Cecil;
using MonoInterop.DotNet;
using Scriban;

namespace MonoInterop;

public class DLLInfo
{
    public string name;
    public string File;
}
public class DLLReader
{
    static void EmitByNamespace(StringBuilder sb, IEnumerable<Class> classes, Action<Class, int> emit)
    {
        string open = "";
        foreach (var c in classes)
        {
            string ns = c.Namespace;
            if (ns != open)
            {
                if (open != "") sb.AppendLine("}").AppendLine();
                if (ns != "") sb.AppendLine($"namespace {ns.Replace(".", "::")} {{");
                open = ns;
            }
            emit(c, ns == "" ? 0 : 1);
        }
        if (open != "") sb.AppendLine("}").AppendLine();
    }
    public static Dictionary<string, Class> ClassMap = new();
    public static List<Class> Classes = new();
    public static Dictionary<string, string> HelperFunctions = new();
    public static HashSet<string> RootAssemblies = new();
    public static void ConvertModule(ModuleDefinition module, bool IsRoot)
    {
        foreach (var type in module.Types)
        {
            if (type.IsCompilerGenerated() && !Options.GlobalOptions.IncludeCompiledTypes) continue;
            if(type.IsPrimitive() && !Options.GlobalOptions.IncludePrimitives) continue;
            Classes.AddRange(Class.FromType(type, IsRoot));         // fills NestedClasses and DependantClasses
        }
    }

    public static string BuildModules()
    {
        if (RootAssemblies.Count > 0)
        {
            int Count = Classes.Count;
            var Needed = Sorter.ComputeNeeded();
            Classes.RemoveAll((c) => !Needed.Contains(c));
            Console.WriteLine($"Removed {Count - Classes.Count} unnecessary types");
        }
        var sorted = Sorter.SortClasses(Classes);

        var sb = new StringBuilder();
        sb.AppendLine(Constants.DefaultInclude);
        sb.AppendLine();
        Console.WriteLine("writing forward declerations ");
        // Phase 1: forward declarations, grouped by namespace so each namespace appears once
        sb.AppendLine("// ---- forward declarations ----");
        var grouped = Classes.GroupBy(c => c.Namespace ?? "").SelectMany(g => g);
        EmitByNamespace(sb, grouped, (c, i) => c.WriteForward(sb, i));
        
        Console.WriteLine("writing class definitions ");
        // Phase 2: definitions in dependency order (nested classes are written inside their outer)
        sb.AppendLine("// ---- class definitions ----");
        EmitByNamespace(sb, sorted, (c, i) => c.WriteBody(sb, i));
        
        Console.WriteLine("writing method definitions ");
        // Phase 3: method bodies, any order
        sb.AppendLine("// ---- definitions ----");
        EmitByNamespace(sb, Classes, (c, i) => c.WriteDefinitions(sb, i, ""));

        return sb.ToString();
    }
    /*public static IEnumerable<DLLInfo> ReadDLLs(DirectoryInfo dir)
    {
        foreach (FileInfo file in dir.EnumerateFiles())
        {
            if (file.Extension != ".dll")
            {
                continue;
            }
            Console.WriteLine($"Processing {file}");
            DLLInfo info = new DLLInfo();
            info.name = file.Name.Replace(".dll", ".h");
            try
            {
                ModuleDefinition asm = ModuleDefinition.ReadModule(file.FullName);
                info.File = ConvertModule(asm);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to parse DLL {file}! due to {e} writing stub....");
                while (e.InnerException != null) e = e.InnerException;
                Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                
                info.File = $" failed to parse due to {e} ";
                if (!Options.GlobalOptions.IgnoreExceptions)
                {
                    throw new InvalidOperationException("stopped MonoInterop, ignoring exceptions is disabled.", e);
                }
            }
            yield return info;
        }
    }*/
    public static DLLInfo ReadDLLsAsOne(DirectoryInfo dir)
    {
        if (Options.GlobalOptions.RootAssemblies != null)
        {
            RootAssemblies = new HashSet<string>(Options.GlobalOptions.RootAssemblies.Split(", "));
        }
        DLLInfo info = new DLLInfo();
        info.name = "MonoDLLs.h";
        foreach (FileInfo file in dir.EnumerateFiles())
        {
            if (file.Extension != ".dll")
            {
                if (file.Extension == ".h") //helper function
                {
                    HReader.ParseHeader(file.FullName);
                }
                continue;
            }
            bool IsRoot = RootAssemblies.Contains(file.Name);
            string msg = IsRoot ? "Root Assembly" : "";
            Console.WriteLine($"Processing {msg} {file}");
            try
            {
                ModuleDefinition asm = ModuleDefinition.ReadModule(file.FullName);
                ConvertModule(asm, IsRoot);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to parse DLL {file}! due to {e} writing stub....");
                while (e.InnerException != null) e = e.InnerException;
                Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                if (!Options.GlobalOptions.IgnoreExceptions)
                {
                    throw new InvalidOperationException("stopped MonoInterop, ignoring exceptions is disabled.", e);
                }
            }
        }
        info.File = BuildModules();
        return info;
    }
}