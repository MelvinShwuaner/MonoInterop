using System.Text;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Scriban;

namespace MonoInterop.DotNet;

public class Param(string Type, string Name, Method.ParamType paramType, bool primitiveptr)
{
   public string Name = Name;
   public string Type = (primitiveptr ? "const " : "") + Type;                 // element type, without '&'
   public string FullName => ParamType switch
   {
      Method.ParamType.Normal => $"const {Type}& {Name}",
      Method.ParamType.Ref => $"{Type}& {Name}",
      _ => $"{Type} {Name}"
   };
   public override string ToString()
   {
      return FullName;
   }
   public Method.ParamType ParamType = paramType;                  // ByReferenceType
   
   //public string Decl => IsRef ? $"{Type}& {Name}" : $"const {Type}& {Name}";
   //public string Arg  => IsRef ? (IsWrapper ? $"&mi_ref_{Name}" : $"MonoTraits<{Type}>::ToArg({Name})")
    //  : $"MonoTraits<{Type}>::ToArg({Name})";
}
public class Method
{
   public enum ParamType
   {
      Normal,
      Ref,
      Ptr
   }
   public enum Type
   {
      Normal = 0,
      SemiConstructor = 1, //operators (this <- other). didnt want to make them constructors as they can conflict with actual constructors.
      Constructor = 2,
      SemiOperator = 3,
      Operator = 4
   }
   public int Token;
   public string Name;
   public string SigName;
   public string ReturnType => ReturnName == "" ? Name : ReturnName;
   public string ReturnName;
   private string Properties = "";
   public string PostProperties = "";
   public List<Param> Parameters = new ();
   public List<string> GenericParameters = new ();
   public bool IsCtor;
   public bool IsVoid => ReturnName is "void" or "";
   public bool IsStatic => Properties.Contains("static");
   public bool IsVirtual;
   public bool ReverseSig => type == Type.SemiOperator;
   public Type type;
   public bool IsFreeOperator => type ==  Type.Operator && IsStatic;
   public Method(MethodDefinition methodDefinition, Class Class)
   {
      Name = methodDefinition.CheckMethod(Class, out Properties, out PostProperties, out type) ?? methodDefinition.Name.ToCPP(false).CheckSpecialNames();
      IsCtor = type == Type.Constructor;
      IsVirtual = methodDefinition.IsVirtual;
      ReturnName = type switch
      {
         Type.SemiConstructor => Class.GetNameWithTemplate(),
         Type.Normal or Type.SemiOperator or Type.Operator => Class.AddAndReturn(methodDefinition.ReturnType).CppName(),
         _ => ""
      };
      if (IsFreeOperator)
      {
         SigName = Name;
      }
      else
      {
         SigName = $"{Class.GetNameWithTemplate()}::{Name}";
      }
      //DependantClasses.Add(methodDefinition.ReturnType.FullName.ToCPP());
      Token = methodDefinition.MetadataToken.ToInt32();
      if (methodDefinition.HasGenericParameters)
      {
         GenericParameters.AddRange(methodDefinition.GenericParameters.Select(p => p.Name.ToCPP()));
      }
      if(methodDefinition.Parameters.Count > 0 && type != Type.SemiOperator)
      {
         for (int i = 0; i < methodDefinition.Parameters.Count; i++)
         {
            var p = methodDefinition.Parameters[i];
            
            if (p.ParameterType is GenericParameter g)
            {
               if (g.Owner == methodDefinition.DeclaringType && Utils.NeedsNonDeduced(methodDefinition))
               {
                  string Name = $"G_{g.Name}";
                  if(!GenericParameters.Contains(Name))
                     GenericParameters.Add(Name);
                  Parameters.Add(new Param($"MonoInterop::Generic<{Name}>", p.Name.CheckSpecialNames(), ParamType.Normal, false));
                  continue;
               }
            }
            Parameters.Add(new Param(Class.AddAndReturn(p.ParameterType).CppName().StripRefs(out var paramType), p.Name.CheckSpecialNames(), paramType, p.ParameterType.IsPrimitivePtr()));
         }
      }
   }

   public string GetDecleration()
   {
      StringBuilder sb = new StringBuilder();
      if (GenericParameters.Count > 0)
      {
         sb.Append("template <");
         sb.Append(string.Join(", ", GenericParameters.Select(name => "class " + (name.StartsWith("G_") ? $"{name} = {name.Replace("G_", "")}" : name))));
         sb.AppendLine(">");
      }
      sb.Append(Properties + ' ');
      if (!ReverseSig)
      {
         sb.Append(ReturnName + " ");
         sb.Append(Name);
      }
      else
      {
         sb.Append(Name + " ");
         sb.Append(ReturnName);
      }

      sb.Append('(');
      sb.Append(string.Join(", ", Parameters));
      sb.Append(") ");
      sb.Append(PostProperties);
      sb.Append(';');
      return sb.ToString();
   }
}