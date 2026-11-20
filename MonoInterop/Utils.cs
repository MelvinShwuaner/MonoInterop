using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using MonoInterop.DotNet;

namespace MonoInterop;

public static class Utils
{
    static bool MentionsClassParam(TypeReference t)
    {
        t = RealType(t); // your existing helper

        switch (t)
        {
            case GenericParameter gp:
                return gp.Type == GenericParameterType.Type;   // class-level, not method-level

            case TypeSpecification ts when t is not GenericInstanceType: // array, byref, pointer, etc.
                return MentionsClassParam(ts.ElementType);

            case GenericInstanceType gi:
                return gi.GenericArguments.Any(MentionsClassParam);

            default:
                return false;
        }
    }

    static bool MentionsClassParam(MethodDefinition m) =>
        m.Parameters.Any(p => MentionsClassParam(p.ParameterType));

    /// True only when this method must get the NonDeduced<U_T> rewrite:
    /// it is in a generic class, its parameters mention a class generic parameter,
    /// and a sibling overload (same name, same arity) mentions none.
    public static bool NeedsNonDeduced(MethodDefinition m, Func<MethodDefinition, bool>? isEmitted = null)
    {
        var type = m.DeclaringType;
        if (!type.HasGenericParameters && type.DeclaringType == null) return false; // fast exit
        if (!MentionsClassParam(m)) return false;

        return type.Methods.Any(other =>
            !ReferenceEquals(other, m) &&
            other.Name == m.Name &&
            other.IsStatic == m.IsStatic &&
            other.Parameters.Count == m.Parameters.Count &&
            (isEmitted == null || isEmitted(other)) &&   // ignore overloads you dropped or filtered
            !MentionsClassParam(other));
    }
    public static bool IsCompilerGenerated(this MemberReference type)
    {
        return type.Name.Contains('<') || type.Name.Contains('>');
    }

    public static bool IsPrimitive(this TypeReference type)
    {
        return type.IsPrimitive || type.MetadataType == MetadataType.Void;
    }
    public static TypeReference RealType(this TypeReference type)
    {
        while (type is IModifierType mod)    
            type = mod.ElementType;
        return type;
    }
    public static string CutNamespace(this string name)
    {
        int depth = 0, cut = -1;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ':' && depth == 0 && i + 1 < name.Length && name[i + 1] == ':')
            {
                cut = i + 1;   // index of the second ':'
                i++;           // skip the pair
            }
        }
        return cut < 0 ? name : name.Substring(cut + 1);
    }

    public static string CheckMaps(string Name)
    {
        return Constants.AssemblyMaps.GetValueOrDefault(Name, Name.Replace('.', '_')+ "_dll");
    }
    public static string CheckConflict(this TypeReference t, string ID)
    {
        if (DLLReader.ClassMap.TryGetValue(ID, out Class klass))
        {
            if (klass.Conflict == Class.ConflictOption.Original)
            {
                TypeDefinition def = t.Resolve();
                return def.Module.Name.Replace('.', '_') + "::" + ID;
            }
        }
        return "::"+ID;
    }
    public static string[] CppID(this TypeReference t) //no modifiers/ generics/ etc
    {
        t = t.RealType();                        // modreq/modopt, from earlier
        if (t.IsPrimitive())
        {
            return [];
        }
        switch (t)
        {
            case ArrayType at when at.IsVector:       // T[] (rank 1, zero-based)
                return at.ElementType.CppID();

            case ArrayType at:                        // T[,] etc.
                // no multi-dim support yet: pick a policy (skip method, or MArrayND<T, rank>)
                return at.ElementType.CppID();

            case ByReferenceType br:                  // ref/out/in
                return br.ElementType.CppID();

            case PointerType pt:
                return pt.ElementType.CppID();

            case GenericInstanceType gi:
                List<string> types = new List<string>();
                types.AddRange(gi.ElementType.CppID());
                foreach (var type in gi.GenericArguments.Select(CppID))
                {
                    types.AddRange(type);
                }
                return [.. types];

            case GenericParameter gp:
                return [];

            default:
                return [t.CheckConflict(t.FullName.ToCPP())]; 
        }
    }
    public static string CppName(this TypeReference t)
    {
        t = t.RealType();                        // modreq/modopt, from earlier
        if (t.IsPrimitive())
        {
            if (t.MetadataType == MetadataType.Void)
            {
                return "void";
            }
            return t.FullName.Replace("System.", "MonoInterop::");
        }
        switch (t)
        {
            case ArrayType at when at.IsVector:       // T[] (rank 1, zero-based)
                return $"MonoInterop::MArray<{CppName(at.ElementType)}>";

            case ArrayType at:                        // T[,] etc.
                // no multi-dim support yet: pick a policy (skip method, or MArrayND<T, rank>)
                return $"MonoInterop::MArrayND<{CppName(at.ElementType)}, {at.Rank}>";

            case ByReferenceType br:                  // ref/out/in
                return CppName(br.ElementType) + "&";

            case PointerType pt:
                return CppName(pt.ElementType) + "*";

            case GenericInstanceType gi:
                var args = string.Join(", ", gi.GenericArguments.Select(CppName));   // recursion handles A<B[]>
                return $"{CppName(gi.ElementType)}<{args}>";

            case GenericParameter gp:
                return gp.Name;

            default:
                return t.CheckConflict(t.FullName.ToCPP());      // primitives, flattened names, Namespace::Name
        }
    }
    public static TypeReference StripGenericArguments(this TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType genericInstance)
        {
            // Returns the open generic TypeReference (e.g., List`1)
            return genericInstance.ElementType;
        }

        return typeRef;
    }

    public static bool IsConsistent(this PropertyDefinition p)
    {
        if (p.SetMethod != null)
        {
            var set = p.SetMethod;
            if (set.Parameters.Count != p.Parameters.Count + 1)
            {
                return false;
            }
            var value = set.Parameters[set.Parameters.Count - 1].ParameterType;
            if (value.FullName != p.PropertyType.FullName) return false;
        }
        if (p.GetMethod != null)
        {
            var get = p.GetMethod;
            if (get != null && get.Parameters.Count != p.Parameters.Count) return false;
        }
        return true;
    }
    public static void AppendTemplate(StringBuilder sb, string pad, List<string>? GenericParameters)
    {
        if (GenericParameters == null || GenericParameters.Count == 0)
        {
            return;
        }
        sb.Append(pad).Append("template<");
        for (int i = 0; i < GenericParameters.Count; i++)
        {
            sb.Append("class " + GenericParameters[i]);
            if (i < GenericParameters.Count - 1)
            {
                sb.Append(", ");
            }
        }
        sb.AppendLine(">");
    }
    public static string BuildTemplate(List<string>? GenericParameters)
    {
        StringBuilder sb = new();
        if (GenericParameters == null || GenericParameters.Count == 0)
        {
            return "";
        }
        sb.Append("template<");
        for (int i = 0; i < GenericParameters.Count; i++)
        {
            sb.Append("class " + GenericParameters[i]);
            if (i < GenericParameters.Count - 1)
            {
                sb.Append(", ");
            }
        }
        sb.Append('>');
        return sb.ToString();
    }

    public static bool isPrivateProperty(this PropertyDefinition property)
    {
       return (property.GetMethod == null || property.GetMethod.IsPrivate) &&
            (property.SetMethod == null || property.SetMethod.IsPrivate);
    }

    public static bool IsExplicitImpl(this string ilName) => ilName.Contains('.');//from interfaces
    static bool TakesInDeclaringType(this MethodReference method)
    {
        TypeReference Type = method.Parameters[0].ParameterType;
        TypeReference declaringType = method.DeclaringType;

        // Exclude arrays (MyType[]) and ref/out modifiers (MyType&)
        if (Type.IsArray || Type.IsPointer)
            return false;

        // Unwraps GenericInstanceType (MyType`1<T> -> MyType`1) without resolving
        TypeReference returnElement = Type.GetElementType();
        TypeReference declaringElement = declaringType.GetElementType();

        if (returnElement == declaringElement)
            return true;

        return returnElement.FullName == declaringElement.FullName 
               && returnElement.Scope.Name == declaringElement.Scope.Name;
    }
    public static string? CheckMethod(this MethodDefinition method, Class Class, out string PreProperty, out string PostProperty, out Method.Type Type)
    {
        PostProperty = "";
        PreProperty = "";
        Type = Method.Type.Normal;
        if (method.Name == ".ctor")
        {
            Type = Method.Type.Constructor;
            return Class.Name;
        }
        if (method.Name == "op_Explicit")
        {
            if (!method.TakesInDeclaringType())
            {
                PreProperty = "static";
                Type = Method.Type.SemiConstructor;
                return $"From{method.Parameters[0].ParameterType.FullName.ToCPP(Namespaces:false, true)}";
            }
            
            Type = Method.Type.SemiOperator;
            PostProperty = "const";
            PreProperty = "explicit";
            return "operator";
        }
        if (method.Name == "op_Implicit")
        {
            if (!method.TakesInDeclaringType())
            {
                PreProperty = "static";
                Type = Method.Type.SemiConstructor;
                return $"From{method.Parameters[0].ParameterType.FullName.ToCPP(Namespaces:false, true)}";
            }
            
            Type = Method.Type.SemiOperator;
            PostProperty = "const";
            return "operator";
        }

        if (Constants.OperatorMap.TryGetValue(method.Name, out var Name))
        {
            Type = Method.Type.Operator;
            if (method.IsStatic)
            {
                PreProperty = "static";
            }
            return Name;
        }
        if (method.IsStatic)
        {
            PreProperty = "static";
        }
        else
        {
            PostProperty = "const";
        }
        return null;
    }
    public static string CheckSpecialNames(this string Name)
    {
        if (Constants.SpecialNames.Contains(Name))
        {
            return $"_{Name}_";
        }
        return Name;
    }
    public static string ToName(this string OriginalName)
    {
        return OriginalName.Replace("+", "__").Replace('`', '_');
    }

    public static bool IsPrimitivePtr(this TypeReference type)
    {
        return type is PointerType ptr 
               && ptr.ElementType.IsPrimitive();
    }

    public static string StripSpecials(this string Name)
    {
        return Name.Replace("&", "").Replace("*", "");
    }
    public static string StripRefs(this string TypeName, out Method.ParamType Type)
    {
        if (TypeName.EndsWith('&'))
        {
            Type = Method.ParamType.Ref;
            return TypeName[..^1];
        }
        if (TypeName.EndsWith('*'))
        {
            Type = Method.ParamType.Ptr;
            return TypeName;
        }
        Type = Method.ParamType.Normal;
        return TypeName;
    }
    public static string ToCPP(this string TypeName, bool Namespaces = true, bool StripGenerics = false, bool StripSpecials = true)
    {
        if (StripGenerics)
        {
            TypeName = TypeName.Replace('<', '_').Replace('>', '_');
        }
        if (Namespaces)
        {
            TypeName = TypeName.Replace(".", "::");
        }
        else
        {
            TypeName = TypeName.Replace(".", "_").Replace("::", "_");
        }
        if (StripSpecials)
        {
            TypeName = TypeName.Replace("&", "ref").Replace("*", "ptr").Replace("[]", "arr");
        }
        else
        {
            TypeName = TypeName.Replace("[]", "");
        }
        return TypeName.Replace('`', '_').Replace("/", "__");
    }

    public static bool IsGeneric(this Class Class)
    {
        return Class.GenericParameters.Count > 0; 
    }
}