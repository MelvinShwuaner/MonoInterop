using Mono.Cecil;

namespace MonoInterop.DotNet;

public class Property
{
    public Property(PropertyDefinition propertyDefinition, Class Class, string Name)
    {
        IsStatic = !propertyDefinition.HasThis;
        this.Name = Name;
        Type = Class.AddAndReturn(propertyDefinition.PropertyType).CppName().StripRefs(out _);
        Getter = propertyDefinition.GetMethod?.Name.ToCPP(false).CheckSpecialNames() ?? "";
        Setter = propertyDefinition.SetMethod?.Name.ToCPP(false).CheckSpecialNames() ?? "";
        if (propertyDefinition.HasParameters)
        {
            IsIndexer = true;
            Parameters = string.Join(", ", propertyDefinition.Parameters.Select(p => Class.AddAndReturn(p.ParameterType).CppName()));
            ParamNames = string.Join(", ", propertyDefinition.Parameters.Select(p => p.Name.CheckSpecialNames()));
            this.Name += Parameters.ToCPP(false, true).Replace(", ", "_");
        }
    }
    public string Name, Type, Getter, Setter, Parameters = "", ParamNames = "";
    public string Args => IsIndexer ? string.Join(", ",
        Parameters.Split(", ")
            .Zip(ParamNames.Split(", "), (a, b) => $"{a} const& {b}")) : "";
    public string Params => string.Join(", ",
        Parameters.Split(", ")
            .Zip(ParamNames.Split("; "), (a, b) => $"{a} const& {b}"));
    
    public string ParamNamesAdd => ParamNames == "" ? "" :  ParamNames + ", ";
    public string ParametersAdd => Parameters == "" ? "" : ", " + Parameters;
    public string ArgsAdd => Args == " " ? "" : ", " + Args;
    public bool IsIndexer, IsStatic;
}