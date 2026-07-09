using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Hydrator;

public static class MemberRenamer
{
    public static void RenameField(FieldDefinition definition, Utf8String newName)
    {
        var oldName = definition.Name;
        definition.Name = newName;

        foreach (var type in Module.GetAllTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasMethodBody)
                    continue;

                if (method.CilMethodBody is not { } body)
                    continue;

                foreach (var instruction in body.Instructions)
                    if (instruction.Operand is MemberReference memberReference)
                        if (memberReference.Name == oldName)
                            if (memberReference.DeclaringType!.TryResolve(Context, out var resolvedDeclaringType))
                                if (resolvedDeclaringType == definition.DeclaringType)
                                    if (SignatureComparer.Default.Equals(memberReference.Signature, definition.Signature))
                                        memberReference.Name = newName;
            }
        }
    }

    public static void RenameMethod(MethodDefinition definition, Utf8String newName)
    {
        var oldName = definition.Name;
        definition.Name = newName;

        foreach (var type in Module.GetAllTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasMethodBody)
                    continue;

                if (method.CilMethodBody is not { } body)
                    continue;

                foreach (var instruction in body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MemberReference memberReference:
                            {
                                if (memberReference.Name == oldName)
                                    if (memberReference.DeclaringType!.TryResolve(Context, out var resolvedDeclaringType))
                                        if (resolvedDeclaringType == definition.DeclaringType)
                                            if (SignatureComparer.Default.Equals(memberReference.Signature, definition.Signature))
                                                    memberReference.Name = newName;
                                break;
                            }
                        case MethodSpecification methodSpecification:
                            {
                                if (methodSpecification.TryResolve(Context, out var resolvedMethod))
                                    if (resolvedMethod == definition)
                                        if (methodSpecification.Method is MemberReference memberReference)
                                            if (memberReference.Name == oldName)
                                                if (memberReference.DeclaringType!.TryResolve(Context, out var resolvedDeclaringType))
                                                    if (resolvedDeclaringType == definition.DeclaringType)
                                                        if (SignatureComparer.Default.Equals(memberReference.Signature, definition.Signature))
                                                                memberReference.Name = newName;
                                break;
                            }
                    }
                }
            }
        }
    }
}