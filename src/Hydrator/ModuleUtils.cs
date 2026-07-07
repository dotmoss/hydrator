using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using System;

namespace Hydrator;

public static class ModuleUtils
{
    static string BuildFlattenTypeName(TypeDefinition type)
    {
        var name = NameUtils.GetClearTypeName(type) + ("`" + type.GenericParameters.Count);
        while ((type = type.DeclaringType!) is not null)
            name = NameUtils.GetClearTypeName(type) + ("_" + name);

        return name;
    }

    static TypeDefinition GetTopDeclaringType(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
            type = type.DeclaringType;

        return type;
    }

    static TypeAttributes MapVisibility(TypeAttributes attributes)
    {
        var newAttributes = attributes & ~TypeAttributes.VisibilityMask;
        if ((attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic)
            return newAttributes | TypeAttributes.Public;

        return newAttributes;
    }

    public static void FlattenTypes()
    {
        var nestedTypes = TypeResolver.ResolveAllNestedTypes()
            .OrderBy(type =>
            {
                var depth = 0;
                while ((type = type!.DeclaringType!) is not null)
                    depth++;
                return depth;
            });

        foreach (var type in nestedTypes)
        {
            if (VisibilityUtils.IsExternallyVisibleAndLibrary(type))
                continue;

            var name = BuildFlattenTypeName(type);
            type.Namespace = GetTopDeclaringType(type).Namespace;
            type.Name = name;
            type.Attributes = MapVisibility(type.Attributes);
            type.DeclaringType!.NestedTypes.Remove(type);
            Module.TopLevelTypes.Add(type);
        }
    }

    public static void DefineIgnoresAccessChecksToAttribute(string[] assemblyNames)
    {
        var attributeRef = new TypeReference(
            Module.CorLibTypeFactory.CorLibScope,
            "System",
            "Attribute"
        );

        var type = new TypeDefinition(
            "System.Runtime.CompilerServices",
            "IgnoresAccessChecksToAttribute",
            TypeAttributes.Public | TypeAttributes.Class,
            attributeRef
        );

        var attributeUsageTypeRef = new TypeReference(Module.CorLibTypeFactory.CorLibScope, "System", "AttributeUsageAttribute");
        var attributeTargetsTypeRef = new TypeReference(Module.CorLibTypeFactory.CorLibScope, "System", "AttributeTargets");

        var attributeUsage = attributeUsageTypeRef.ToTypeSignature(false);
        var attributeTargetsSignature = attributeTargetsTypeRef.ToTypeSignature(true);

        var attributeUsageCtorRef = new MemberReference(
            attributeUsageTypeRef,
            ".ctor",
            MethodSignature.CreateInstance(Module.CorLibTypeFactory.Void, [attributeUsage])
        );

        var targetArgument = new CustomAttributeArgument(attributeTargetsSignature, 1);
        CustomAttributeSignature attributeUsageSignature;

        if (assemblyNames.Length > 1)
        {
            var allowMultipleNamedArgument = new CustomAttributeNamedArgument(
                CustomAttributeArgumentMemberType.Property,
                "AllowMultiple",
                Module.CorLibTypeFactory.Boolean,
                new CustomAttributeArgument(Module.CorLibTypeFactory.Boolean, true)
            );
            attributeUsageSignature = new CustomAttributeSignature([targetArgument], [allowMultipleNamedArgument]);
        }
        else
        {
            attributeUsageSignature = new CustomAttributeSignature([targetArgument]);
        }

        var attributeUsageAttribute = new CustomAttribute(attributeUsageCtorRef, attributeUsageSignature);
        type.CustomAttributes.Add(attributeUsageAttribute);

        var baseCtorRef = new MemberReference(
            attributeRef,
            ".ctor",
            MethodSignature.CreateInstance(Module.CorLibTypeFactory.Void)
        );

        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName,
            MethodSignature.CreateInstance(Module.CorLibTypeFactory.Void, [Module.CorLibTypeFactory.String])
        );
        var body = ctor.CilMethodBody = new CilMethodBody();
        var instructions = body.Instructions;
        instructions.Add(CilOpCodes.Ldarg_0);
        instructions.Add(CilOpCodes.Call, baseCtorRef);
        instructions.Add(CilOpCodes.Ret);

        type.Methods.Add(ctor);
        Module.TopLevelTypes.Add(type);

        foreach (var assemblyName in assemblyNames)
        {
            var customAttribute = new CustomAttribute(
                ctor,
                new CustomAttributeSignature(new CustomAttributeArgument(Module.CorLibTypeFactory.String, assemblyName))
            );

            Module.Assembly!.CustomAttributes.Add(customAttribute);
        }
    }
}