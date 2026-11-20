using Mono.Cecil;

namespace MonoInterop.DotNet;

public class Property
{
    public Property(PropertyDefinition propertyDefinition, Class Class)
    {
        IsStatic = !propertyDefinition.HasThis;
        Name = propertyDefinition.Name.ToCPP(false).CheckSpecialNames();
        if (Name == Class.Name)
        {
            Name = $"_{Name}_";
        }
        Type = Class.AddAndReturn(propertyDefinition.PropertyType).CppName();
        Getter = propertyDefinition.GetMethod?.Name.ToCPP(false).CheckSpecialNames() ?? "";
        Setter = propertyDefinition.SetMethod?.Name.ToCPP(false).CheckSpecialNames() ?? "";
        if (propertyDefinition.HasParameters)
        {
            IsIndexer = !(propertyDefinition.Name.IsExplicitImpl() && propertyDefinition.isPrivateProperty()); //private interface properties mean they shouldn't be used and cause conflicts
            Parameters = string.Join(", ", propertyDefinition.Parameters.Select(p => Class.AddAndReturn(p.ParameterType).CppName()));
            ParamNames = string.Join(", ", propertyDefinition.Parameters.Select(p => p.Name.CheckSpecialNames()));
            Name += Parameters.ToCPP(false, true).Replace(", ", "_");
        }
        foreach (var other in Class.Properties) //yes, IL can have two properties of the same name :wbstupid:
        {
            if (other.Name == Name)
            {
                IsIndexer = false;
                Name += "_"+Type.ToCPP(false, true);
            }
        }
    }
    public string Name, Type, Getter, Setter, Parameters = "", ParamNames = "";
    public string Args => string.Join(", ",
        Parameters.Split(", ")
            .Zip(ParamNames.Split(", "), (a, b) => $"{a} {b}"));
    
    public string ParamNamesAdd => ParamNames == "" ? "" :  ParamNames + ", ";
    public string ParametersAdd => Parameters == "" ? "" : ", " + Parameters;
    public string ArgsAdd => Args == " " ? "" : ", " + Args;
    public bool IsIndexer, IsStatic;
}