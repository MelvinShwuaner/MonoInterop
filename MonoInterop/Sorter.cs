using MonoInterop.DotNet;
using static MonoInterop.DLLReader;
namespace MonoInterop;

public class Sorter
{
    static readonly IEqualityComparer<Class> RefCmp =
        (IEqualityComparer<Class>)ReferenceEqualityComparer.Instance;

    public static Class? GetClass(string fullname)
    {
        return DLLReader.ClassMap.GetValueOrDefault(fullname);
    }
// Sorts one sibling list (top-level, or one class's NestedClasses), then recurses into each.
    public static  List<Class> SortClasses(List<Class> classes)
    {
        var deps = new Dictionary<Class, List<Class>>(RefCmp);
        foreach (var c in classes)
        {
            var list = new List<Class>();
            if (c.ParentID != null)
            {
                var parent = GetClass(c.ParentID);
                if (parent != null && !list.Contains(parent, RefCmp))
                    list.Add(parent);
            }
            if (c.Interfaces != null)
            {
                foreach (var face in c.Interfaces)
                {
                    var parent = GetClass(face.Item2);
                    if (parent != null && !list.Contains(parent, RefCmp))
                        list.Add(parent);
                }
            }
            deps[c] = list;
        }

        var result = new List<Class>();
        var state = new Dictionary<Class, int>(RefCmp); // 1 = visiting, 2 = done

        void Visit(Class c)
        {
            if (state.TryGetValue(c, out var s))
            {
                if (s == 1) Console.Error.WriteLine($"Dependency cycle at {c.FullName}, edge ignored");
                return;
            }

            state[c] = 1;
            foreach (var d in deps[c]) Visit(d);
            state[c] = 2;
            result.Add(c);
        }

        foreach (var c in classes) Visit(c);
        

        return result;
    }
    public static HashSet<Class> ComputeNeeded()
    {
        var needed = new HashSet<Class>();
        var work = new Stack<Class>();

        void Add(Class c)
        {
            if (needed.Add(c))
                work.Push(c);
        }

        foreach (var c in ClassMap.Values)
            if (c.IsRoot) Add(c);

        while (work.Count > 0)
        {
            var c = work.Pop();
            foreach (var name in c.ReferencedClasses){
                if (ClassMap.TryGetValue(name, out var dep))
                {
                    Add(dep);
                }
                else
                {
                    Console.WriteLine($"Missing Referenced: {name}");
                }
            }
        }

        return needed;
    }
}