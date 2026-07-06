using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Hydrator;

public static class VisibilityUtils
{
    public static void SetVisibility(TypeDefinition type, TypeAttributes attributes) => type.Attributes = type.Attributes & ~TypeAttributes.VisibilityMask | attributes;
    public static void SetVisibility(FieldDefinition field, FieldAttributes attributes) => field.Attributes = field.Attributes & ~FieldAttributes.FieldAccessMask | attributes;
    public static void SetVisibility(MethodDefinition method, MethodAttributes attributes) => method.Attributes = method.Attributes & ~MethodAttributes.MemberAccessMask | attributes;

    public static void SetPublicVisibility(TypeDefinition type) => SetVisibility(type, TypeAttributes.Public);
    public static void SetPublicVisibility(FieldDefinition field) => SetVisibility(field, FieldAttributes.Public);
    public static void SetPublicVisibility(MethodDefinition method) => SetVisibility(method, MethodAttributes.Public);

    public static void SetInternalVisibility(TypeDefinition type) => SetVisibility(type, TypeAttributes.NotPublic);
    public static void SetInternalVisibility(FieldDefinition field) => SetVisibility(field, FieldAttributes.Assembly);
    public static void SetInternalVisibility(MethodDefinition method) => SetVisibility(method, MethodAttributes.Assembly);

    public static bool IsExternallyVisibleAndLibrary(TypeDefinition type)
    {
        if (Options.IsApplication)
            return false;

        while (type is not null)
        {
            if (type.IsNested)
            {
                if (!type.IsNestedPublic)
                    return false;

                type = type.DeclaringType;
            }
            else
            {
                return type.IsPublic;
            }
        }

        return false;
    }

    public static void SolveVisibilityConflicts()
    {
        if (Options.IsApplication)
            SolveVisibilityConflictsForApplication();
        else SolveVisibilityConflictsForLibrary();
    }

    static void SolveVisibilityConflictsForApplication()
    {
        foreach (var type in Module.TopLevelTypes)
        {
            SetVisibility(type, TypeAttributes.Public);

            foreach (var field in type.Fields)
                SetVisibility(field, FieldAttributes.Public);

            foreach (var method in type.Methods)
                SetVisibility(method, MethodAttributes.Public);
        }
    }

    static void SolveVisibilityConflictsForLibrary()
    {
        var context = Module.RuntimeContext!;
        var externModules = new List<ModuleDefinition>();

        foreach (var type in Module.GetAllTypes())
        {
            if (type.BaseType is not null)
            {
                var baseType = type.BaseType.Resolve(context);
                if (baseType is not null)
                    SolveForType(type, baseType!);
            }

            foreach (var generic in type.GenericParameters)
                SolveForAttributes(type, generic.CustomAttributes);                

            SolveForAttributes(type, type.CustomAttributes);

            foreach (var field in type.Fields)
            {
                foreach (var signatureType in SignatureUtils.ResolveAllTypeDefinitions(field.Signature!.FieldType))
                    SolveForType(type, signatureType);

                SolveForAttributes(type, field.CustomAttributes);
            }

            foreach (var method in type.Methods)
            {
                foreach (var signatureType in SignatureUtils.ResolveAllTypeDefinitions(method.Signature!.ReturnType))
                    SolveForType(type, signatureType);

                foreach (var parameter in method.Signature!.ParameterTypes)
                    foreach (var signatureType in SignatureUtils.ResolveAllTypeDefinitions(parameter))
                        SolveForType(type, signatureType);

                foreach (var genericParameter in method.GenericParameters)
                    foreach (var constraint in genericParameter.Constraints)
                        if (constraint.Constraint!.Resolve(context) is { } resolvedConstraint)
                            SolveForType(type, resolvedConstraint);

                SolveForAttributes(type, method.CustomAttributes);
            }
        }

        if (externModules.Count > 0)
        {
            var externModulesNames = externModules.Select(module => module.Name!.ToString()).ToArray();
            ModuleUtils.DefineIgnoresAccessChecksToAttribute(externModulesNames);
        }

        void SolveForAttributes(TypeDefinition contextType, IList<CustomAttribute> attributes)
        {
            foreach (var attribute in attributes)
            {
                var attributeType = attribute.Type!.Resolve(context);
                if (attributeType is not null)
                    SolveForType(contextType, attributeType);
            }
        }

        void SolveForType(TypeDefinition contextType, TypeDefinition targetType)
        {
            if (!AccessUtils.CanAccessType(contextType, targetType))
                MakeTypeVisible(contextType, targetType);
        }

        void MakeTypeVisible(TypeDefinition contextType, TypeDefinition targetType)
        {
            if (contextType.DeclaringModule == targetType.DeclaringModule)
            {
                do
                {
                    if (!targetType.IsPublic)
                        SetInternalVisibility(targetType);
                }
                while ((targetType = targetType.DeclaringType!) is not null);
            }
            else
            {
                if (!externModules.Contains(targetType.DeclaringModule!))
                    externModules.Add(targetType.DeclaringModule!);
            }
        }
    }
}