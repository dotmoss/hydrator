using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;

namespace Hydrator;

public static class ModuleMerger
{
    public static void MergeModules(List<ModuleDefinition> modules)
    {
        var typeNamespacesAndNames = new Dictionary<string, List<string>>();
        var originalTypesToMerged = new Dictionary<TypeDefinition, TypeDefinition>();
        var originalMembersToMerged = new Dictionary<IMemberDefinition, IMemberDefinition>();
        var originalMethodsToMerged = new Dictionary<MethodDefinition, MethodDefinition>();
        var originalFieldsToMerged = new Dictionary<FieldDefinition, FieldDefinition>();
        var originalPropertiesToMerged = new Dictionary<PropertyDefinition, PropertyDefinition>();
        var originalEventsToMerged = new Dictionary<EventDefinition, EventDefinition>();
        var original01CCtors = new List<MethodDefinition>();

        var moduleTypes = Module.GetAllTypes();
        var merged01Cctor = Module.GetOrCreateModuleConstructor();
        foreach (var type in Module.TopLevelTypes)
        {
            var @namespace = type.Namespace??string.Empty;

            if (!typeNamespacesAndNames.TryGetValue(@namespace, out var names))
                typeNamespacesAndNames.Add(@namespace, names = new List<string>());

            names.Add(type.Name!);
        }

        foreach (var module in modules)
        {
            foreach (var type in module.TopLevelTypes)
            {
                if (type.IsModuleType)
                    continue;

                var typeName = type.Name ?? string.Empty;
                if (typeNamespacesAndNames.TryGetValue(type.Namespace ?? string.Empty, out var names))
                {
                    var originalName = typeName;
                    var counter = 0;
                    while (names.Contains(typeName))
                        typeName = originalName + counter++;
                }

                var mergedType = new TypeDefinition(type.Namespace, typeName, type.Attributes);
                originalTypesToMerged.Add(type, mergedType);
                Module.TopLevelTypes.Add(mergedType);

                if (type.HasNestedTypes)
                {
                    var allNestedTypes = new List<(TypeDefinition MergedTypeOwner, IList<TypeDefinition> NestedOriginalTypes)>();

                    var firstNestedOriginalTypesToMerged = new List<TypeDefinition>();
                    foreach (var nestedType in type.NestedTypes)
                        firstNestedOriginalTypesToMerged.Add(nestedType);
                    allNestedTypes.Add((mergedType, firstNestedOriginalTypesToMerged));

                    var nestedTypesIndex = 0;
                    while (nestedTypesIndex != allNestedTypes.Count)
                    {
                        var (mergedTypeOwner, nestedOriginalTypes) = allNestedTypes[nestedTypesIndex];
                        foreach (var nestedOriginalType in nestedOriginalTypes)
                        {
                            var nestedMergedType = new TypeDefinition(nestedOriginalType.Namespace, nestedOriginalType.Name, nestedOriginalType.Attributes);
                            originalTypesToMerged.Add(nestedOriginalType, nestedMergedType);
                            originalMembersToMerged.Add(nestedOriginalType, nestedMergedType);
                            mergedTypeOwner.NestedTypes.Add(nestedMergedType);

                            if (nestedOriginalType.HasNestedTypes)
                                allNestedTypes.Add((nestedMergedType, nestedOriginalType.NestedTypes));
                        }

                        nestedTypesIndex++;
                    }
                }
            }

            var moduleGlobalClass = module.GetModuleType();
            if (moduleGlobalClass is not null)
                originalTypesToMerged.Add(moduleGlobalClass, Module.GetOrCreateModuleType());
        }

        var typeSemantics = new Dictionary<IList<MethodSemantics>, IList<MethodSemantics>>();
        foreach (var (originalType, mergedType) in originalTypesToMerged)
        {
            typeSemantics.Clear();

            if (!mergedType.IsModuleType)
                if (originalType.ClassLayout is { } layout)
                    mergedType.ClassLayout = new ClassLayout(layout.PackingSize, layout.ClassSize);

            foreach (var property in originalType.Properties)
                typeSemantics.Add(property.Semantics, new List<MethodSemantics>());

            foreach (var @event in originalType.Events)
                typeSemantics.Add(@event.Semantics, new List<MethodSemantics>());

            foreach (var originalField in originalType.Fields)
            {
                var mergedFieldSignature = GetMergedFieldSignature(originalField.Signature!);
                var mergedField = new FieldDefinition(originalField.Name, originalField.Attributes, mergedFieldSignature);
                mergedType.Fields.Add(mergedField);
                mergedField.ImplementationMap = MergeImplementationMap(originalField.ImplementationMap);
                mergedField.MarshalDescriptor = MergeMarshalDescriptor(originalField.MarshalDescriptor);
                mergedField.Constant = MergeConstant(originalField.Constant);
                mergedField.FieldRva = MergeFieldRva(originalField.FieldRva);
                mergedField.FieldOffset = originalField.FieldOffset;

                originalFieldsToMerged.Add(originalField, mergedField);
                originalMembersToMerged.Add(originalField, mergedField);
            }

            foreach (var originalMethod in originalType.Methods)
            {
                if (mergedType.IsModuleType && originalMethod.IsStatic && originalMethod.IsConstructor)
                {
                    original01CCtors.Add(originalMethod);
                    continue;
                }

                var mergedMethodSignature = GetMergedMethodSignature(originalMethod.Signature!);
                var mergedMethod = new MethodDefinition(originalMethod.Name, originalMethod.Attributes, mergedMethodSignature);
                mergedType.Methods.Add(mergedMethod);
                mergedMethod.ImplementationMap = MergeImplementationMap(originalMethod.ImplementationMap);
                mergedMethod.ImplAttributes = originalMethod.ImplAttributes;
                mergedMethod.Parameters.PullUpdatesFromMethodSignature();

                if (originalMethod.Semantics is not null)
                {
                    MergeMethodSemantics(originalMethod, mergedMethod);
                    var mergedSemantics = typeSemantics[originalMethod.Semantics.Association!.Semantics];
                    mergedSemantics.Add(mergedMethod.Semantics!);
                }

                originalMethodsToMerged.Add(originalMethod, mergedMethod);
                originalMembersToMerged.Add(originalMethod, mergedMethod);
            }

            foreach (var originalProperty in originalType.Properties)
            {
                var mergedPropertySignature = MergePropertySignature(originalProperty.Signature!);
                var mergedProperty = new PropertyDefinition(originalProperty.Name, originalProperty.Attributes, mergedPropertySignature);
                mergedType.Properties.Add(mergedProperty);
                mergedProperty.Constant = MergeConstant(originalProperty.Constant);

                MergeSemantics(typeSemantics[originalProperty.Semantics], mergedProperty);

                originalPropertiesToMerged.Add(originalProperty, mergedProperty);
                originalMembersToMerged.Add(originalProperty, mergedProperty);
            }

            foreach (var originalEvent in originalType.Events)
            {
                var mergedEventType = GetMergedTypeDefOrRef(originalEvent.EventType!);
                var mergedEvent = new EventDefinition(originalEvent.Name, originalEvent.Attributes, mergedEventType);
                mergedType.Events.Add(mergedEvent);

                MergeSemantics(typeSemantics[originalEvent.Semantics], mergedEvent);

                originalEventsToMerged.Add(originalEvent, mergedEvent);
                originalMembersToMerged.Add(originalEvent, mergedEvent);
            }
        }

        foreach (var (originalType, mergedType) in originalTypesToMerged)
        {
            if (mergedType.IsModuleType)
                continue;

            MergeCustomAttributes(originalType, mergedType);

            if (originalType.BaseType is { } baseType)
                mergedType.BaseType = GetMergedTypeDefOrRef(baseType);

            foreach (var implementation in originalType.Interfaces)
                mergedType.Interfaces.Add(MergeInterfaceImplementation(implementation));

            foreach (var implementation in originalType.MethodImplementations)
            {
                mergedType.MethodImplementations.Add(new MethodImplementation(
                    GetMergedMethodDefOrRef(implementation.Declaration!),
                    GetMergedMethodDefOrRef(implementation.Body!)
                ));
            }

            MergeCustomAttributes(originalType, mergedType);
            MergeGenericParameters(originalType, mergedType);
            MergeSecurityDeclarations(originalType, mergedType);
        }

        foreach (var (originalField, mergedField) in originalFieldsToMerged)
        {
            MergeCustomAttributes(originalField, mergedField);
        }

        foreach (var (originalMethod, mergedMethod) in originalMethodsToMerged)
        {
            MergeParameterDefinitions(originalMethod, mergedMethod);
            MergeSecurityDeclarations(originalMethod, mergedMethod);
            MergeGenericParameters(originalMethod, mergedMethod);
            MergeCustomAttributes(originalMethod, mergedMethod);
        }

        foreach (var (originalProperty, mergedProperties) in originalPropertiesToMerged)
        {
            MergeCustomAttributes(originalProperty, mergedProperties);
        }

        foreach (var (originalEvent, mergedEvent) in originalEventsToMerged)
        {
            MergeCustomAttributes(originalEvent, mergedEvent);
        }

        foreach (var type in moduleTypes)
        {
            foreach (var method in type.Methods)
            {
                originalMethodsToMerged.Add(method, method);
                originalMembersToMerged.Add(method, method);
            }
        }

        MergeMethodsWithoutPrematureReturns(merged01Cctor, original01CCtors);

        foreach (var (originalMethod, mergedMethod) in originalMethodsToMerged)
            MergeMethodBody(originalMethod, mergedMethod);

        ITypeDefOrRef GetMergedTypeDefOrRef(ITypeDefOrRef? defOrRef)
        {
            switch (defOrRef)
            {
                case TypeDefinition originalTypeDefinition:
                    if (originalTypesToMerged.TryGetValue(originalTypeDefinition, out var mergedTypeDefinition))
                        return mergedTypeDefinition;
                    return originalTypeDefinition;

                case TypeReference typeReference:
                    if (typeReference.Resolve(Context) is { } resolvedDefinition)
                        if (originalTypesToMerged.TryGetValue(resolvedDefinition, out var mergedDefinition))
                            return mergedDefinition;
                    return typeReference;

                case TypeSpecification typeSpecification:
                    if (GetMergedTypeSignature(typeSpecification.Signature!) is { } mergedSignature)
                        return new TypeSpecification(mergedSignature);
                    return typeSpecification;

                default:
                    return defOrRef!;
            }
        }

        IMethodDefOrRef GetMergedMethodDefOrRef(IMethodDefOrRef? defOrRef)
        {
            switch (defOrRef)
            {
                case MethodDefinition originalDefinition:
                    {
                        if (originalMethodsToMerged.TryGetValue(originalDefinition, out var mergedDefinition))
                            return mergedDefinition;
                        return originalDefinition;
                    }
                case MemberReference memberReference:
                    {
                        if (memberReference.Resolve(Context) is { } resolvedMemberDefinition)
                            if (resolvedMemberDefinition is MethodDefinition resolvedMethodDefinition)
                                if (originalMethodsToMerged.TryGetValue(resolvedMethodDefinition, out var mergedDefinition))
                                    if (mergedDefinition is IMethodDefOrRef mergedDefOrRef)
                                        return mergedDefOrRef;

                        return new MemberReference(GetMergedTypeDefOrRef(defOrRef.DeclaringType), defOrRef.Name, GetMergedMethodSignature(defOrRef.Signature!));
                    }
                default:
                    return defOrRef!;
            }
        }

        IMethodDescriptor GetMergedMethodDescriptor(IMethodDescriptor? descriptor)
        {
            switch (descriptor)
            {
                case MemberReference reference:
                    {
                        return GetMergedMethodReference(reference);
                    }
                case IMethodDefOrRef defOrRef:
                    {
                        return GetMergedMethodDefOrRef(defOrRef);
                    }
                case MethodSpecification specification:
                    {
                        var memberRef = GetMergedMethodDefOrRef(specification.Method);

                        var instantiation = new GenericInstanceMethodSignature();
                        foreach (var argument in specification.Signature!.TypeArguments)
                            instantiation.TypeArguments.Add(GetMergedTypeSignature(argument));

                        return new MethodSpecification(memberRef, instantiation);
                    }
                default:
                    throw null!;
            }
        }

        IFieldDescriptor GetMergedFieldDescriptor(IFieldDescriptor? descriptor)
        {
            switch (descriptor)
            {
                case FieldDefinition originalDefinition:
                    {
                        if (originalFieldsToMerged.TryGetValue(originalDefinition, out var mergedDefinition))
                            return mergedDefinition;
                        return originalDefinition;
                    }
                case MemberReference memberReference:
                    {
                        if (memberReference.Resolve(Context) is { } resolvedMemberDefinition)
                            if (resolvedMemberDefinition is FieldDefinition resolvedFieldDefinition)
                                if (originalFieldsToMerged.TryGetValue(resolvedFieldDefinition, out var mergedDefinition))
                                    if (mergedDefinition is IFieldDescriptor mergedDefOrRef)
                                        return mergedDefOrRef;

                        return new MemberReference(GetMergedTypeDefOrRef(descriptor.DeclaringType as ITypeDefOrRef), descriptor.Name, GetMergedFieldSignature(descriptor.Signature!));
                    }
                default:
                    return descriptor!;
            }
        }

        ICustomAttributeType GetMergedCustomAttributeType(ICustomAttributeType customAttributeType)
        {
            switch (customAttributeType)
            {
                case MethodDefinition originalMethodDefinition:
                    if (originalMethodsToMerged.TryGetValue(originalMethodDefinition, out var mergedMethodDefinition))
                        return mergedMethodDefinition;
                    return originalMethodDefinition;

                case MemberReference memberReference:
                    if (memberReference.Resolve(Context) is { } resolvedDefinition)
                        if (originalMembersToMerged.TryGetValue(resolvedDefinition, out var mergedDefinition))
                            if (mergedDefinition is ICustomAttributeType mergedCustomAttributeType)
                                return mergedCustomAttributeType;
                    return memberReference;

                default:
                    return customAttributeType;
            }
        }

        TypeSignature GetMergedTypeSignature(TypeSignature? originalSignature)
        {
            switch (originalSignature)
            {
                case TypeDefOrRefSignature typeDefOrRefSignature:
                    if (typeDefOrRefSignature.Type is TypeDefinition originalTypeDefinition)
                        if (originalTypesToMerged.TryGetValue(originalTypeDefinition, out var mergedTypeDefinition))
                            return mergedTypeDefinition.ToTypeSignature();

                    if (typeDefOrRefSignature.Type is TypeReference typeReference)
                    {
                        var resolvedDefinition = typeReference.Resolve(Context);
                        if (resolvedDefinition is not null && originalTypesToMerged.TryGetValue(resolvedDefinition, out var mappedDefinition))
                            return mappedDefinition.ToTypeSignature();
                    }

                    return typeDefOrRefSignature;

                case GenericInstanceTypeSignature genericInstanceSignature:
                    var mergedGenericInstance = new GenericInstanceTypeSignature(GetMergedTypeDefOrRef(genericInstanceSignature.GenericType), genericInstanceSignature.IsValueType);

                    foreach (var argument in genericInstanceSignature.TypeArguments)
                        mergedGenericInstance.TypeArguments.Add(GetMergedTypeSignature(argument));

                    return mergedGenericInstance;

                case ArrayTypeSignature arraySignature:
                    var mergedArray = new ArrayTypeSignature(GetMergedTypeSignature(arraySignature.BaseType));

                    foreach (var dimension in arraySignature.Dimensions)
                        mergedArray.Dimensions.Add(dimension);

                    return mergedArray;

                default:
                    return originalSignature switch
                    {
                        null => null!,
                        SzArrayTypeSignature szArraySignature => new SzArrayTypeSignature(GetMergedTypeSignature(szArraySignature.BaseType)),
                        PointerTypeSignature pointerSignature => new PointerTypeSignature(GetMergedTypeSignature(pointerSignature.BaseType)),
                        ByReferenceTypeSignature byRefSignature => new ByReferenceTypeSignature(GetMergedTypeSignature(byRefSignature.BaseType)),
                        CustomModifierTypeSignature modifierSignature => new CustomModifierTypeSignature(modifierSignature.ModifierType, modifierSignature.IsRequired, GetMergedTypeSignature(modifierSignature.BaseType)),
                        FunctionPointerTypeSignature functionPointerSignature => new FunctionPointerTypeSignature(GetMergedMethodSignature(functionPointerSignature.Signature)),
                        GenericParameterSignature genericParameterSignature => new GenericParameterSignature(genericParameterSignature.ContextModule, genericParameterSignature.ParameterType, genericParameterSignature.Index),
                        PinnedTypeSignature pinnedSignature => new PinnedTypeSignature(GetMergedTypeSignature(pinnedSignature.BaseType)),
                        SentinelTypeSignature sentinelSignature => sentinelSignature,
                        CorLibTypeSignature corLibSignature => corLibSignature,
                        _ => originalSignature
                    };
            }
        }

        FieldSignature GetMergedFieldSignature(FieldSignature originalSignature)
        {
            var mergedTypeSignature = GetMergedTypeSignature(originalSignature.FieldType);
            return new FieldSignature(originalSignature.CallingConvention, mergedTypeSignature);
        }

        MethodSignature GetMergedMethodSignature(MethodSignature originalSignature)
        {
            var originalParameters = originalSignature!.ParameterTypes;
            var parameterTypes = new TypeSignature[originalParameters.Count];
            for (int i = 0; i < parameterTypes.Length; i++)
                parameterTypes[i] = GetMergedTypeSignature(originalParameters[i]);

            var mergedSignature = new MethodSignature(originalSignature.Attributes, GetMergedTypeSignature(originalSignature.ReturnType), parameterTypes);
            mergedSignature.GenericParameterCount = originalSignature.GenericParameterCount;

            foreach (var sentinelParameterType in originalSignature.SentinelParameterTypes)
                mergedSignature.SentinelParameterTypes.Add(GetMergedTypeSignature(sentinelParameterType));

            return mergedSignature;
        }

        IFieldDescriptor GetMergedFieldReference(MemberReference memberReference)
        {
            if (memberReference.TryResolve(Context, out var memberDefinition))
            {
                if (memberDefinition.DeclaringModule == Module)
                    return (IFieldDescriptor)memberDefinition;

                if (memberDefinition is FieldDefinition fieldDefinition)
                    if (originalFieldsToMerged.TryGetValue(fieldDefinition, out var mergedField))
                        return mergedField;
            }

            return new MemberReference(GetMergedTypeDefOrRef(memberReference.DeclaringType), memberReference.Name, GetMergedFieldSignature(((IFieldDescriptor)memberReference).Signature!));
        }

        IMethodDescriptor GetMergedMethodReference(MemberReference memberReference)
        {
            if (memberReference.TryResolve(Context, out var memberDefinition))
            {
                if (memberDefinition.DeclaringModule == Module)
                    return (IMethodDescriptor)memberDefinition;

                if (memberDefinition is MethodDefinition methodDefinition)
                    if (originalMethodsToMerged.TryGetValue(methodDefinition, out var mergedMethod))
                        return mergedMethod;
            }

            return new MemberReference(GetMergedTypeDefOrRef(memberReference.DeclaringType), memberReference.Name, GetMergedMethodSignature(((IMethodDescriptor)memberReference).Signature!));
        }

        GenericInstanceMethodSignature GetMergedGenericInstanceMethodSignature(GenericInstanceMethodSignature originalSignature)
        {
            var typeArguments = new TypeSignature[originalSignature.TypeArguments.Count];
            for (int i = 0; i < typeArguments.Length; i++)
                typeArguments[i] = GetMergedTypeSignature(originalSignature.TypeArguments[i]);

            return new GenericInstanceMethodSignature(originalSignature.Attributes, typeArguments);
        }

        PropertySignature MergePropertySignature(PropertySignature propertySignature)
        {
            var parameterTypes = new TypeSignature[propertySignature.ParameterTypes.Count];
            for (int i = 0; i < parameterTypes.Length; i++)
                parameterTypes[i] = GetMergedTypeSignature(propertySignature.ParameterTypes[i]);

            return new PropertySignature(propertySignature.Attributes, GetMergedTypeSignature(propertySignature.ReturnType), parameterTypes);
        }

        Constant? MergeConstant(Constant? originalConstant)
        {
            if (originalConstant is null)
                return null;

            DataBlobSignature? mergedValue = null;
            if (originalConstant.Value is not null)
                mergedValue = new DataBlobSignature(originalConstant.Value.Data);

            return new Constant(originalConstant.Type, mergedValue);
        }

        ImplementationMap? MergeImplementationMap(ImplementationMap? originalImplementationMap)
        {
            if (originalImplementationMap is null)
                return null;

            var importer = new ReferenceImporter(Module);
            var module = importer.ImportModule(originalImplementationMap.Scope!);
            return new ImplementationMap(module, originalImplementationMap.Name, originalImplementationMap.Attributes);
        }

        MarshalDescriptor? MergeMarshalDescriptor(MarshalDescriptor? originalDescriptor) =>
            originalDescriptor switch
            {
                null => null,
                ComInterfaceMarshalDescriptor comInterfaceDescriptor => new ComInterfaceMarshalDescriptor(comInterfaceDescriptor.NativeType),
                CustomMarshalDescriptor customDescriptor => new CustomMarshalDescriptor(customDescriptor.Guid, customDescriptor.NativeTypeName, GetMergedTypeSignature(customDescriptor.MarshalType), customDescriptor.Cookie),
                FixedSysStringMarshalDescriptor fixedSysStringDescriptor => new FixedSysStringMarshalDescriptor(fixedSysStringDescriptor.Size),
                LPArrayMarshalDescriptor lpArrayDescriptor => new LPArrayMarshalDescriptor(lpArrayDescriptor.ArrayElementType),
                SafeArrayMarshalDescriptor safeArrayDescriptor => new SafeArrayMarshalDescriptor(safeArrayDescriptor.VariantType, safeArrayDescriptor.VariantTypeFlags, GetMergedTypeSignature(safeArrayDescriptor.UserDefinedSubType)),
                SimpleMarshalDescriptor simpleDesriptor => new SimpleMarshalDescriptor(simpleDesriptor.NativeType),
                _ => throw null!
            };

        ISegment? MergeFieldRva(ISegment? originalFieldRva) =>
            originalFieldRva switch
            {
                null => null,
                IReadableSegment readableSegment => new DataSegment(readableSegment.ToArray()),
                ICloneable cloneable => (ISegment)cloneable.Clone(),
                _ => throw null!
            };

        CustomAttributeArgument MergeCustomAttributeArgument(CustomAttributeArgument originalArgument)
        {
            var mergedArgument = new CustomAttributeArgument(GetMergedTypeSignature(originalArgument.ArgumentType));
            mergedArgument.IsNullArray = originalArgument.IsNullArray;

            for (int i = 0; i < originalArgument.Elements.Count; i++)
                mergedArgument.Elements.Add(MergeElement(originalArgument.Elements[i]));

            return mergedArgument;

            object? MergeElement(object? element)
            {
                if (element is TypeSignature signature)
                    return GetMergedTypeSignature(signature);

                return element;
            }
        }

        InterfaceImplementation MergeInterfaceImplementation(InterfaceImplementation originalImplementation)
        {
            var mergedImplementation = new InterfaceImplementation(GetMergedTypeDefOrRef(originalImplementation.Interface));
            MergeCustomAttributes(originalImplementation, mergedImplementation);
            return mergedImplementation;
        }

        void MergeCustomAttributes(IHasCustomAttribute source, IHasCustomAttribute destination)
        {
            foreach (var customAttribute in source.CustomAttributes)
                destination.CustomAttributes.Add(MergeCustomAttribute(customAttribute));

            CustomAttribute MergeCustomAttribute(CustomAttribute originalAttribute)
            {
                var mergedAttibuteSignature = MergeCustomAttributeSignature(originalAttribute.Signature!);
                var mergedConstructor = GetMergedCustomAttributeType(originalAttribute.Constructor!);

                return new CustomAttribute(mergedConstructor, mergedAttibuteSignature);

                CustomAttributeSignature MergeCustomAttributeSignature(CustomAttributeSignature originalSignature)
                {
                    var mergedSignature = new CustomAttributeSignature();

                    foreach (var argument in originalSignature.FixedArguments)
                        mergedSignature.FixedArguments.Add(MergeCustomAttributeArgument(argument));

                    foreach (var namedArgument in originalSignature.NamedArguments)
                    {
                        var mergedArgument = MergeCustomAttributeArgument(namedArgument.Argument);
                        var mergedNamesArgument = new CustomAttributeNamedArgument(namedArgument.MemberType, namedArgument.MemberName, namedArgument.ArgumentType, mergedArgument);
                        mergedSignature.NamedArguments.Add(mergedNamesArgument);
                    }

                    return mergedSignature;
                }
            }
        }

        void MergeSecurityDeclarations(IHasSecurityDeclaration source, IHasSecurityDeclaration destination)
        {
            foreach (var declaration in source.SecurityDeclarations)
                destination.SecurityDeclarations.Add(MergeSecurityDeclaration(declaration));

            SecurityDeclaration MergeSecurityDeclaration(SecurityDeclaration originalDeclaration)
            {
                return new SecurityDeclaration(originalDeclaration.Action, MergePermissionSetSignature(originalDeclaration.PermissionSet));

                PermissionSetSignature? MergePermissionSetSignature(PermissionSetSignature? originalSignature)
                {
                    if (originalSignature is null)
                        return null;

                    var mergedSignature = new PermissionSetSignature();
                    foreach (var attribute in originalSignature.Attributes)
                        mergedSignature.Attributes.Add(MergeSecurityAttribute(attribute));

                    return mergedSignature;

                    SecurityAttribute MergeSecurityAttribute(SecurityAttribute originalAttribute)
                    {
                        var mergedAttribute = new SecurityAttribute(GetMergedTypeSignature(originalAttribute.AttributeType));

                        foreach (var argument in originalAttribute.NamedArguments)
                        {
                            var newArgument = new CustomAttributeNamedArgument(
                                argument.MemberType,
                                argument.MemberName,
                                GetMergedTypeSignature(argument.ArgumentType),
                                MergeCustomAttributeArgument(argument.Argument));

                            mergedAttribute.NamedArguments.Add(newArgument);
                        }

                        return mergedAttribute;
                    }
                }
            }
        }

        void MergeParameterDefinitions(MethodDefinition source, MethodDefinition destination)
        {
            foreach (var parameter in source.ParameterDefinitions)
                destination.ParameterDefinitions.Add(MergeParameter(parameter));

            ParameterDefinition MergeParameter(ParameterDefinition originalParameter)
            {
                var mergedParameterDef = new ParameterDefinition(originalParameter.Sequence, originalParameter.Name, originalParameter.Attributes);
                mergedParameterDef.Constant = MergeConstant(originalParameter.Constant);
                mergedParameterDef.MarshalDescriptor = MergeMarshalDescriptor(originalParameter.MarshalDescriptor);
                return mergedParameterDef;
            }
        }

        void MergeGenericParameters(IHasGenericParameters source, IHasGenericParameters destination)
        {
            foreach (var genericParameter in source.GenericParameters)
                destination.GenericParameters.Add(MergeGenericParameter(genericParameter));

            GenericParameter MergeGenericParameter(GenericParameter originalParameter)
            {
                var mergedParameter = new GenericParameter(originalParameter.Name, originalParameter.Attributes);
                MergeCustomAttributes(originalParameter, mergedParameter);

                foreach (var constraint in originalParameter.Constraints)
                    mergedParameter.Constraints.Add(MergeGenericParameterConstraint(constraint));

                return mergedParameter;

                GenericParameterConstraint MergeGenericParameterConstraint(GenericParameterConstraint originalConstraint)
                {
                    var mergedConstraint = new GenericParameterConstraint(GetMergedTypeDefOrRef(originalConstraint.Constraint));
                    MergeCustomAttributes(originalConstraint, mergedConstraint);
                    return mergedConstraint;
                }
            }
        }

        void MergeSemantics(IList<MethodSemantics> source, IHasSemantics destinatoin)
        {
            foreach (var semantic in source)
                destinatoin.Semantics.Add(new MethodSemantics(semantic.Method, semantic.Attributes));
        }

        void MergeMethodSemantics(MethodDefinition source, MethodDefinition destination)
        {
            destination.Semantics = new MethodSemantics(destination, source.Semantics!.Attributes);
        }

        void MergeMethodBody(MethodDefinition source, MethodDefinition destination)
        {
            if (source.FullName == "System.Void Target1.Program::Main(System.String[])")
            {

            }

            if (source.CilMethodBody is { } sourceMethodBody)
            {
                var destinationMethodBody = destination.CilMethodBody = new CilMethodBody
                {
                    InitializeLocals = sourceMethodBody.InitializeLocals,
                    MaxStack = sourceMethodBody.MaxStack
                };

                MergeLocalVariables(sourceMethodBody, destinationMethodBody);
                MergeCilInstructions(sourceMethodBody, destinationMethodBody);
                MergeExceptionHandlers(sourceMethodBody, destinationMethodBody);
            }

            if (source.NativeMethodBody is { } nativeMethodBody)
            {

            }

            void MergeLocalVariables(CilMethodBody source, CilMethodBody destination)
            {
                foreach (var variable in source.LocalVariables)
                {
                    var clonedVariable = new CilLocalVariable(GetMergedTypeSignature(variable.VariableType));
                    destination.LocalVariables.Add(clonedVariable);
                }
            }

            void MergeCilInstructions(CilMethodBody source, CilMethodBody destination)
            {
                var branches = new List<CilInstruction>();
                var switches = new List<CilInstruction>();

                foreach (var instruction in source.Instructions)
                {
                    var clonedInstruction = CloneInstruction(destination, instruction);
                    if (clonedInstruction.OpCode.Code == CilCode.Switch)
                        switches.Add(clonedInstruction);
                    else if (clonedInstruction.IsBranch())
                        branches.Add(clonedInstruction);
                    destination.Instructions.Add(clonedInstruction);
                }

                foreach (var branch in branches)
                {
                    var label = (ICilLabel)branch.Operand!;
                    branch.Operand = destination.Instructions.GetLabel(label.Offset);
                }

                foreach (var @switch in switches)
                {
                    var labels = (IList<ICilLabel>)@switch.Operand!;
                    var clonedLabels = new List<ICilLabel>(labels.Count);

                    for (int i = 0; i < labels.Count; i++)
                        clonedLabels.Add(destination.Instructions.GetLabel(labels[i].Offset));

                    @switch.Operand = clonedLabels;
                }

                CilInstruction CloneInstruction(CilMethodBody destinationBody, CilInstruction instruction)
                {
                    var clonedInstruction = new CilInstruction(instruction.Offset, instruction.OpCode);
                    switch (instruction.OpCode.OperandType)
                    {
                        case CilOperandType.InlineBrTarget:
                        case CilOperandType.ShortInlineBrTarget:
                        case CilOperandType.InlineSwitch:
                            clonedInstruction.Operand = instruction.Operand;
                            break;

                        case CilOperandType.InlineField when instruction.Operand is IFieldDescriptor field:
                            clonedInstruction.Operand = GetMergedFieldDescriptor(field);
                            break;

                        case CilOperandType.InlineMethod when instruction.Operand is IMethodDescriptor method:
                            clonedInstruction.Operand = GetMergedMethodDescriptor(method);
                            break;

                        case CilOperandType.InlineSig when instruction.Operand is StandAloneSignature standAlone:
                            clonedInstruction.Operand = new StandAloneSignature(standAlone.Signature switch
                            {
                                MethodSignature signature => GetMergedMethodSignature(signature),
                                GenericInstanceMethodSignature signature => GetMergedGenericInstanceMethodSignature(signature),
                                _ => throw null!
                            });
                            break;

                        case CilOperandType.InlineType when instruction.Operand is ITypeDefOrRef type:
                            clonedInstruction.Operand = GetMergedTypeDefOrRef(type);
                            break;

                        case CilOperandType.InlineI:
                        case CilOperandType.InlineI8:
                        case CilOperandType.InlineNone:
                        case CilOperandType.InlineR:
                        case CilOperandType.InlineString:
                        case CilOperandType.ShortInlineI:
                        case CilOperandType.ShortInlineR:
                        case CilOperandType.InlineField:
                        case CilOperandType.InlineMethod:
                        case CilOperandType.InlineType:
                            clonedInstruction.Operand = instruction.Operand;
                            break;

                        case CilOperandType.InlineTok:
                            clonedInstruction.Operand = CloneInlineTokOperand(instruction);
                            break;

                        case CilOperandType.InlineVar:
                        case CilOperandType.ShortInlineVar:
                            clonedInstruction.Operand = instruction.Operand is CilLocalVariable local
                                ? destinationBody.LocalVariables[local.Index]
                                : instruction.Operand;
                            break;

                        case CilOperandType.InlineArgument:
                        case CilOperandType.ShortInlineArgument:
                            clonedInstruction.Operand = instruction.Operand is Parameter parameter
                                ? destinationBody.Owner!.Parameters.GetBySignatureIndex(parameter.MethodSignatureIndex)
                                : instruction.Operand;
                            break;

                        default:
                            throw null!;
                    }

                    return clonedInstruction;
                }

                object CloneInlineTokOperand(CilInstruction instruction) =>
                    instruction.Operand switch
                    {
                        ITypeDefOrRef type => GetMergedTypeDefOrRef(type),
                        MemberReference { IsField: true } field => GetMergedFieldReference(field),
                        MemberReference { IsMethod: true } method => GetMergedMethodReference(method),
                        MethodDefinition method => GetMergedMethodDefOrRef(method),
                        FieldDefinition field => GetMergedFieldDescriptor(field),
                        _ => throw null!
                    };
            }

            void MergeExceptionHandlers(CilMethodBody source, CilMethodBody destination)
            {
                foreach (var handler in source.ExceptionHandlers)
                {
                    destination.ExceptionHandlers.Add(new CilExceptionHandler
                    {
                        HandlerType = handler.HandlerType,
                        TryStart = ToClonedLabel(handler.TryStart),
                        TryEnd = ToClonedLabel(handler.TryEnd),
                        HandlerStart = ToClonedLabel(handler.HandlerStart),
                        HandlerEnd = ToClonedLabel(handler.HandlerEnd),
                        FilterStart = ToClonedLabel(handler.FilterStart),
                        ExceptionType = handler.ExceptionType??GetMergedTypeDefOrRef(handler.ExceptionType)
                    });
                }

                ICilLabel? ToClonedLabel(ICilLabel? label) => label is not null
                    ? destination.Instructions.GetLabel(label.Offset)
                    : null;
            }
        }

        void MergeMethodsWithoutPrematureReturns(MethodDefinition destinationMethod, IList<MethodDefinition> sourceMethods)
        {
            var destination = destinationMethod.CilMethodBody!.Instructions;
            destination.RemoveAt(destination.Count - 1);

            foreach (var sourceMethod in sourceMethods)
            {
                var source = sourceMethod.CilMethodBody!.Instructions;
                for (var instIdx = 0; instIdx < source.Count - 1; instIdx++)
                {
                    var instruction = source[instIdx];
                    if (instruction.IsBranch() || instruction.OpCode.Code == CilCode.Ret)
                        throw null!;
                    destination.Add(instruction);
                }
            }

            destination.Add(new CilInstruction(CilOpCodes.Ret));
            destination.CalculateOffsets();
        }
    }
}