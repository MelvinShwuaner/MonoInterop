using System.Text;
using Mono.Cecil;
using Scriban;
using Scriban.Runtime;

namespace MonoInterop.DotNet;
using static DLLReader;
public class Class
{
    public void WriteForward(StringBuilder sb, int indent)
    {
        string pad = new string(' ', indent * 4);
        Utils.AppendTemplate(sb, pad, GenericParameters);
        sb.Append(pad).AppendLine($"class {Name};");
    }

    public TypeReference AddAndReturn(TypeReference Name)
    {
        foreach (var type in Name.CppID())
        {
            ReferencedClasses.Add(type);
        }
        return Name;
    }
    public void WriteBody(StringBuilder sb, int indent)
    {
        string pad = new string(' ', indent * 4);
        Utils.AppendTemplate(sb, pad, GenericParameters);
        sb.Append(pad).Append("class " + Name).Append(" : public ").Append(ParentClass).AppendLine(" {");
        sb.Append(pad).AppendLine("public:");
        
        sb.Append(pad).AppendLine(Constants.InitMethod.Render(new
        {
            image = Image,
            namespaze = Namespace,
            klass = OriginalName,
            GenericParameters,
            pad
        }));
        sb.Append(pad).AppendLine(Constants.Constructor.Render(this));
        foreach (var method in Methods)
        {
            if (method.IsFreeOperator)
                continue; //static operators are free functions
            sb.Append(pad).AppendLine(method.GetDecleration());
        }
        if (HelperFunctions.TryGetValue(FullName, out string helpers))
        {
            Console.WriteLine($"Using Helper functions in {FullName}");
            sb.Append(pad).AppendLine(helpers);
        }
        sb.AppendLine(Constants.PropertyDef.Render(new
        {
            props = Properties,
            self = GetNameWithTemplate(),
            IsGeneric = GenericParameters.Count > 0,
            pad
        }));
        foreach (var p in Fields)
        {
            sb.AppendLine(Constants.FieldDef.Render(new
            {
                p,
                self = GetNameWithTemplate(),
                pad
            }));
        }
        
        
        sb.Append(pad).AppendLine("};");
        sb.AppendLine();
        /* foreach (var line in MemberLines())        // however you print fields/methods now
        // sb.Append(pad).Append("    ").AppendLine(line);*/
        
        
    }

    public string GetNameWithTemplate()
    {
        string name = Name;
        if (GenericParameters.Count > 0)
        {
            name += "<";
            for (int i = 0; i < GenericParameters.Count; i++)
            {
                name += GenericParameters[i];
                if (i < GenericParameters.Count - 1)
                {
                    name += ", ";
                }
            }

            name += ">";
        }
        return name;
    }
    public void WriteDefinitions(StringBuilder sb, int i, string idk)
    {
        string pad = new string(' ', i * 4);
        string cls = GetNameWithTemplate();
        foreach (var m in Methods)
        {
            Utils.AppendTemplate(sb, pad, GenericParameters);
            Utils.AppendTemplate(sb, pad, m.GenericParameters);
            sb.AppendLine(Constants.MethodImplementation.Render(new
            {
                cls,
                m
            }));
        }

        string cls_template = Utils.BuildTemplate(GenericParameters);
        sb.AppendLine(Constants.PropertyImpl.Render(new
        {
            props = Properties,
            self = cls,
            cls_template,
            IsGeneric = GenericParameters.Count > 0,
            pad
        }));
        foreach (var field in Fields)
        {
            sb.AppendLine(Constants.FieldImpl.Render(new
            {
                self = cls,
                cls_template,
                p = field
            }));
        }
    }
    public List<Class> NestedClasses = new();
    public List<Method> Methods = new();
    public List<Property> Properties = new();
    public List<Field> Fields = new();
    public string ParentClass, ParentID = null;
    public string Name;
    public string FullName;
    public string Namespace;
    public string Image;
    public string OriginalName;
    public HashSet<string> ReferencedClasses = new();
    public List<string> GenericParameters = new();
    public bool IsRoot = false;
    public static List<Class> FromType(TypeDefinition type,bool IsRoot, Class? Declaring = null)
    {
        List<Class> List = new();
        Class klass = new Class();
        klass.Image = type.Module.Name;
        klass.IsRoot = IsRoot;
        if (type.HasGenericParameters)
        {
            foreach (var param in type.GenericParameters)
            {
                klass.GenericParameters.Add(param.FullName);
            }
        }

        if (Declaring != null)
        {
            klass.OriginalName = Declaring.OriginalName + "+" + type.Name;
            klass.Namespace = Declaring.Namespace;
        }
        else
        {
            klass.OriginalName = type.Name;
            klass.Namespace = type.Namespace;
        }
        klass.Name = klass.OriginalName.ToName();
        klass.FullName = type.CppName();
        if (type.BaseType == null)
        {
            klass.ParentClass = Constants.RootClass;
        }
        else
        {
            klass.ParentClass = type.BaseType.CppName();
            klass.ParentID = type.BaseType.StripGenericArguments().CppName();
            foreach (var referenced in type.BaseType.CppID())
            {
                klass.ReferencedClasses.Add(referenced);
            }
        }
        foreach (var nested in type.NestedTypes)
        {
            if (nested.IsCompilerGenerated() && !Options.GlobalOptions.IncludeCompiledTypes)
            {
                continue;
            }

            List<Class> Nested = FromType(nested, IsRoot, klass);
            if(Nested.Count > 0)
                klass.NestedClasses.Add(Nested[0]);
            List.AddRange(Nested);
        }
        if (ClassMap.ContainsKey(klass.FullName))
        {
            //Console.Error.WriteLine($"Warning: Class {klass.FullName} has conflicts with another! skipping");
            return List;
        }
        List.Add(klass);
        ClassMap.Add(klass.FullName, klass);
        foreach (var method in type.Methods)
        {
            if (Constants.CutMethod(method))
            {
               // Console.WriteLine($"Cutting Method {method.FullName} from generation");
                continue;
            }
            if (method.IsCompilerGenerated() && !Options.GlobalOptions.IncludeCompiledTypes)
            {
                continue;
            }
            klass.Methods.Add(new Method(method, klass));
        }
        foreach (var property in type.Properties)
        {
            if (property.IsCompilerGenerated() && !Options.GlobalOptions.IncludeCompiledTypes)
            {
                continue;
            }
            if (!property.IsConsistent())
            {
                //Console.WriteLine($"Warning: property {property.FullName} is not consistent! skipping!");
                continue;
            }
            klass.Properties.Add(new Property(property, klass));
        }
        foreach (var field in type.Fields)
        {
            if ((field.IsCompilerGenerated() || field.FieldType.IsCompilerGenerated()) && !Options.GlobalOptions.IncludeCompiledTypes)
            {
                continue;
            }
            klass.Fields.Add(new Field(field, klass));
        }
        return List;
    }
}