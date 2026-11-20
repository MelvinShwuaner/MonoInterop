
using Mono.Cecil;
using Scriban;

namespace MonoInterop.DotNet;

public class Field
{
    public string Name;
    public int Token;
    public bool ReadOnly;
    public bool IsStatic;
    public string Type;
    public Field(FieldDefinition field, Class Class)
    {
        Name = field.Name.CheckSpecialNames();
        if (Name == Class.Name)
        {
            Name = $"_{Name}_";
        }
        ReadOnly = field.IsInitOnly || field.HasConstant;
        IsStatic = field.IsStatic;
        Type = Class.AddAndReturn(field.FieldType).CppName();
        Token = field.MetadataToken.ToInt32();
    }
}