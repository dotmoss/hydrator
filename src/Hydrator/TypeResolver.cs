using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Hydrator;

public static class TypeResolver
{
    public static List<TypeDefinition> ResolveAllGenericParameters()
    {
        var types = new List<TypeDefinition>();

        var processedFullNames = new HashSet<string>();

        foreach (var type in Module.GetAllTypes())
        {
            foreach (var field in type.Fields)
                ExtractAndResolveGenericArguments(field.Signature?.FieldType!, processedFullNames, types);

            foreach (var method in type.Methods)
            {
                if (method.Signature is null)
                    continue;

                ExtractAndResolveGenericArguments(method.Signature.ReturnType, processedFullNames, types);

                foreach (var paramType in method.Signature.ParameterTypes)
                    ExtractAndResolveGenericArguments(paramType, processedFullNames, types);

                if (method.CilMethodBody is not null)
                {
                    foreach (var variable in method.CilMethodBody.LocalVariables)
                        ExtractAndResolveGenericArguments(variable.VariableType, processedFullNames, types);
                }
            }
        }

        return types;

        static void ExtractAndResolveGenericArguments(TypeSignature signature, HashSet<string> processedFullNames, List<TypeDefinition> resultTypes)
        {
            if (signature is null)
                return;

            if (signature is GenericInstanceTypeSignature genericInstance)
            {
                foreach (var argument in genericInstance.TypeArguments)
                {
                    if (argument is null)
                        continue;

                    if (processedFullNames.Add(argument.FullName))
                    {
                        var resolvedType = argument.Resolve(argument.ContextModule?.RuntimeContext);

                        if (resolvedType is not null)
                            resultTypes.Add(resolvedType);
                    }

                    ExtractAndResolveGenericArguments(argument, processedFullNames, resultTypes);
                }
            }

            if (signature is TypeSpecificationSignature typeSpec)
                ExtractAndResolveGenericArguments(typeSpec.BaseType, processedFullNames, resultTypes);
        }
    }

    public static List<TypeDefinition> ResolveAllNestedTypes()
    {
        var nestedTypes = new List<TypeDefinition>();
        foreach (var type in Module.TopLevelTypes)
            ResolveNestedTypes(type, nestedTypes);

        return nestedTypes;

        static void ResolveNestedTypes(TypeDefinition type, List<TypeDefinition> nestedTypes)
        {
            foreach (var nestedType in type.NestedTypes)
            {
                nestedTypes.Add(nestedType);
                ResolveNestedTypes(nestedType, nestedTypes);
            }
        }
    }
}