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

    public static void ConcretizeModuleReferences()
    {
        ProcessCustomAttributes(Module);
        ProcessTypes();
        ProcessFields();
        ProcessMethods();
        ProcessProperties();
        ProcessEvents();

        static void ProcessTypes()
        {
            foreach (var type in Module.GetAllTypes())
            {
                ProcessCustomAttributes(type);
                if (type.BaseType is TypeReference baseReference)
                {
                    var resolvedBase = baseReference.Resolve(Context);
                    if (resolvedBase is not null && resolvedBase.DeclaringModule == Module)
                        type.BaseType = resolvedBase;
                }
                ProcessSignatures(type);
                for (var index = 0; index < type.GenericParameters.Count; index++)
                {
                    var parameter = type.GenericParameters[index];
                    ProcessCustomAttributes(parameter);
                }
            }
        }

        static void ProcessFields()
        {
            foreach (var type in Module.GetAllTypes())
            {
                foreach (var field in type.Fields)
                {
                    ProcessCustomAttributes(field);
                    if (field.Signature is not null)
                        field.Signature.FieldType = ProcessTypeSignature(field.Signature.FieldType);
                }
            }
        }

        static void ProcessMethods()
        {
            foreach (var type in Module.GetAllTypes())
            {
                foreach (var method in type.Methods)
                {
                    ProcessCustomAttributes(method);
                    for (var index = 0; index < method.ParameterDefinitions.Count; index++)
                    {
                        var parameter = method.ParameterDefinitions[index];
                        ProcessCustomAttributes(parameter);
                    }
                    for (var index = 0; index < method.GenericParameters.Count; index++)
                    {
                        var parameter = method.GenericParameters[index];
                        ProcessCustomAttributes(parameter);
                    }

                    if (method.Signature is not null)
                        ProcessMethodSignature(method.Signature);

                    if (method.CilMethodBody is null)
                        continue;

                    ProcessInstructions(method.CilMethodBody);
                    ProcessLocalVariables(method.CilMethodBody);
                }
            }
        }

        static void ProcessProperties()
        {
            foreach (var type in Module.GetAllTypes())
            {
                foreach (var property in type.Properties)
                {
                    ProcessCustomAttributes(property);
                }
            }
        }

        static void ProcessEvents()
        {
            foreach (var type in Module.GetAllTypes())
            {
                foreach (var currentEvent in type.Events)
                {
                    ProcessCustomAttributes(currentEvent);
                }
            }
        }

        static void ProcessMethodSignature(MethodSignature signature)
        {
            if (signature.ReturnType is not null)
                signature.ReturnType = ProcessTypeSignature(signature.ReturnType);

            for (var index = 0; index < signature.ParameterTypes.Count; index++)
            {
                var parameterType = signature.ParameterTypes[index];
                signature.ParameterTypes[index] = ProcessTypeSignature(parameterType);
            }
        }

        static TypeSignature ProcessTypeSignature(TypeSignature signature)
        {
            if (signature is TypeDefOrRefSignature typeDefinitionOrReferenceSignature)
            {
                if (typeDefinitionOrReferenceSignature.Type is TypeReference reference)
                {
                    var resolvedType = reference.Resolve(Context);
                    if (resolvedType is not null && resolvedType.DeclaringModule == Module)
                        return new TypeDefOrRefSignature(resolvedType, typeDefinitionOrReferenceSignature.IsValueType);
                }
            }
            if (signature is GenericInstanceTypeSignature genericSignature)
            {
                for (var index = 0; index < genericSignature.TypeArguments.Count; index++)
                {
                    genericSignature.TypeArguments[index] = ProcessTypeSignature(genericSignature.TypeArguments[index]);
                }
            }
            if (signature is CustomModifierTypeSignature modifierSignature)
            {
                if (modifierSignature.ModifierType is TypeReference reference)
                {
                    var resolvedModifier = reference.Resolve(Context);
                    if (resolvedModifier is not null && resolvedModifier.DeclaringModule == Module)
                        modifierSignature.ModifierType = resolvedModifier;
                }
                modifierSignature.BaseType = ProcessTypeSignature(modifierSignature.BaseType);
            }
            if (signature is TypeSpecificationSignature specificationSignature)
                specificationSignature.BaseType = ProcessTypeSignature(specificationSignature.BaseType);

            return signature;
        }

        static void ProcessInstructions(CilMethodBody body)
        {
            var instructions = body.Instructions;
            for (var index = 0; index < instructions.Count; index++)
            {
                var instruction = instructions[index];
                if (instruction.Operand is TypeReference typeReference)
                {
                    var resolvedType = typeReference.Resolve(Context);
                    if (resolvedType is not null && resolvedType.DeclaringModule == Module)
                        instruction.Operand = resolvedType;
                }
                if (instruction.Operand is MemberReference memberReference)
                {
                    var resolvedMember = memberReference.Resolve(Context);
                    if (resolvedMember is not null && resolvedMember.DeclaringModule == Module)
                        instruction.Operand = resolvedMember;
                }
                if (instruction.Operand is MethodSpecification methodSpecification)
                {
                    if (methodSpecification.Method is MemberReference memberMethodReference)
                    {
                        var resolvedMethod = memberMethodReference.Resolve(Context);
                        if (resolvedMethod is not null && resolvedMethod.DeclaringModule == Module)
                            methodSpecification.Method = (IMethodDefOrRef)resolvedMethod;
                    }
                    if (methodSpecification.Signature is not null)
                    {
                        for (var genericIndex = 0; genericIndex < methodSpecification.Signature.TypeArguments.Count; genericIndex++)
                        {
                            var argument = methodSpecification.Signature.TypeArguments[genericIndex];
                            methodSpecification.Signature.TypeArguments[genericIndex] = ProcessTypeSignature(argument);
                        }
                    }
                }
            }
        }

        static void ProcessLocalVariables(CilMethodBody body)
        {
            foreach (var variable in body.LocalVariables)
            {
                if (variable.VariableType is not null)
                    variable.VariableType = ProcessTypeSignature(variable.VariableType);
            }
        }

        static void ProcessSignatures(TypeDefinition type)
        {
            for (var index = 0; index < type.Interfaces.Count; index++)
            {
                var interfaceImplementation = type.Interfaces[index];
                if (interfaceImplementation.Interface is TypeReference reference)
                {
                    var resolvedInterface = reference.Resolve(Context);
                    if (resolvedInterface is not null && resolvedInterface.DeclaringModule == Module)
                        interfaceImplementation.Interface = resolvedInterface;
                }
            }
        }

        static void ProcessCustomAttributes(IHasCustomAttribute provider)
        {
            for (var index = 0; index < provider.CustomAttributes.Count; index++)
            {
                var attribute = provider.CustomAttributes[index];
                if (attribute.Constructor is MemberReference constructorReference)
                {
                    var resolvedConstructor = constructorReference.Resolve(Context);
                    if (resolvedConstructor is not null && resolvedConstructor.DeclaringModule == Module)
                        attribute.Constructor = (ICustomAttributeType)resolvedConstructor;
                }

                if (attribute.Signature is not null)
                {
                    for (var argumentIndex = 0; argumentIndex < attribute.Signature.FixedArguments.Count; argumentIndex++)
                    {
                        var fixedArgument = attribute.Signature.FixedArguments[argumentIndex];
                        ProcessCustomAttributeArgument(fixedArgument);
                    }
                    for (var argumentIndex = 0; argumentIndex < attribute.Signature.NamedArguments.Count; argumentIndex++)
                    {
                        var namedArgument = attribute.Signature.NamedArguments[argumentIndex];
                        ProcessCustomAttributeArgument(namedArgument.Argument);
                    }
                }
            }
        }

        static void ProcessCustomAttributeArgument(CustomAttributeArgument argument)
        {
            if (argument.ArgumentType is not null)
                argument.ArgumentType = ProcessTypeSignature(argument.ArgumentType);

            if (argument.Element is TypeSignature typeSignatureValue)
                ProcessTypeSignature(typeSignatureValue);
        }
    }
}