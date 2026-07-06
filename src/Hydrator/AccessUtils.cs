using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;

namespace Hydrator;

public static class AccessUtils
{
    public static bool CanAccessType(TypeDefinition contextType, TypeDefinition targetType)
    {
        if (contextType == targetType)
            return true;

        if (targetType.IsNested)
        {
            TypeDefinition declaringType = targetType.DeclaringType;

            if (!CanAccessType(contextType, declaringType))
                return false;

            return CheckNestedAccess(contextType, targetType);
        }
        else
        {
            return CheckTopLevelAccess(contextType, targetType);
        }
    }

    static bool CheckTopLevelAccess(TypeDefinition contextType, TypeDefinition targetType)
    {
        if (targetType.IsPublic)
            return true;

        if (targetType.DeclaringModule == contextType.DeclaringModule)
            return true;

        return false;
    }

    static bool CheckNestedAccess(TypeDefinition contextType, TypeDefinition targetNestedType)
    {
        var visibility = targetNestedType.Attributes & TypeAttributes.VisibilityMask;

        switch (visibility)
        {
            case TypeAttributes.NestedPublic:
                return true;

            case TypeAttributes.NestedPrivate:
                return IsStrictlyEnclosedIn(contextType, targetNestedType.DeclaringType!);

            case TypeAttributes.NestedAssembly:
                return targetNestedType.DeclaringModule == contextType.DeclaringModule;

            case TypeAttributes.NestedFamily:
                return IsSubclassOf(contextType, targetNestedType.DeclaringType!);

            case TypeAttributes.NestedFamilyOrAssembly:
                return targetNestedType.DeclaringModule == contextType.DeclaringModule || IsSubclassOf(contextType, targetNestedType.DeclaringType!);

            case TypeAttributes.NestedFamilyAndAssembly:
                return targetNestedType.DeclaringModule == contextType.DeclaringModule && IsSubclassOf(contextType, targetNestedType.DeclaringType!);

            default:
                return false;
        }
    }

    static bool IsSubclassOf(TypeDefinition typeDefinition, TypeDefinition baseTypeDefinition)
    {
        var currentTypeDefinition = typeDefinition;
        while (currentTypeDefinition is not null)
        {
            if (currentTypeDefinition == baseTypeDefinition)
                return true;

            currentTypeDefinition = currentTypeDefinition.BaseType?.Resolve(Context)!;
        }

        return false;
    }

    static bool IsStrictlyEnclosedIn(TypeDefinition contextType, TypeDefinition enclosingTypeDefinition)
    {
        var currentTypeDefinition = contextType;
        while (currentTypeDefinition is not null)
        {
            if (currentTypeDefinition == enclosingTypeDefinition)
                return true;

            currentTypeDefinition = currentTypeDefinition.DeclaringType!;
        }

        return false;
    }
}