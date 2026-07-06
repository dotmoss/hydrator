using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Hydrator;

public static class SignatureUtils
{
    public static List<TypeDefinition> ResolveAllTypeDefinitions(TypeSignature signature)
    {
        var types = new List<TypeDefinition>();
        ExtractTypeDefinitions(signature, types);

        return types;

        static void ExtractTypeDefinitions(TypeSignature typeSignature, List<TypeDefinition> resultTypes)
        {
            if (typeSignature is TypeSpecificationSignature typeSpecification)
            {
                ExtractTypeDefinitions(typeSpecification.BaseType, resultTypes);
            }
            else if (typeSignature is GenericInstanceTypeSignature genericInstance)
            {
                var resolvedGeneric = genericInstance.GenericType.Resolve(Context);
                if (resolvedGeneric is not null)
                    resultTypes.Add(resolvedGeneric);
                foreach (var argument in genericInstance.TypeArguments)
                    ExtractTypeDefinitions(argument, resultTypes);
            }
            else if (typeSignature is CustomModifierTypeSignature modifierType)
            {
                ExtractTypeDefinitions(modifierType.BaseType, resultTypes);
            }
            else
            {
                var resolvedType = typeSignature.Resolve(Context);
                if (resolvedType is not null)
                    resultTypes.Add(resolvedType);
            }
        }
    }
}