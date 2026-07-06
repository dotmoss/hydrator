using AsmResolver.DotNet;
using AsmResolver.DotNet.Cloning;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.PE.DotNet.Cil;

namespace Hydrator;

public static class ModuleMerger
{
    public static void MergeModules(List<ModuleDefinition> modules)
    {
        var cloner = new MemberCloner(Module);
        foreach (var sourceModule in modules)
            foreach (var type in sourceModule.TopLevelTypes)
                cloner.Include(type, recursive: true);

        cloner.AddListener(new InjectTypeClonerListener(Module));
        cloner.AddListener(new AssignTokensClonerListener(Module));

        cloner.Clone();

        foreach (var sourceModule in modules)
            foreach (var resource in sourceModule.Resources)
                if (!Module.Resources.Contains(resource))
                    Module.Resources.Add(resource);

        ReplaceModuleReferences();
        SolveModulesConflicts();

        void ReplaceModuleReferences()
        {
            var types = Module.TopLevelTypes;
            for (var typeIndex = 0; typeIndex < types.Count; typeIndex = typeIndex + 1)
            {
                var typeDefinition = types[typeIndex];
                ProcessType(typeDefinition);
            }

            void ProcessType(TypeDefinition typeDefinition)
            {
                var methods = typeDefinition.Methods;
                for (var methodIndex = 0; methodIndex < methods.Count; methodIndex = methodIndex + 1)
                {
                    var methodDefinition = methods[methodIndex];
                    if (methodDefinition.CilMethodBody is not null)
                        ProcessMethodBody(methodDefinition.CilMethodBody);
                }

                var nestedTypes = typeDefinition.NestedTypes;
                for (var nestedIndex = 0; nestedIndex < nestedTypes.Count; nestedIndex = nestedIndex + 1)
                {
                    var nestedType = nestedTypes[nestedIndex];
                    ProcessType(nestedType);
                }

                void ProcessMethodBody(CilMethodBody body)
                {
                    var instructions = body.Instructions;
                    for (var instructionIndex = 0; instructionIndex < instructions.Count; instructionIndex = instructionIndex + 1)
                    {
                        var instruction = instructions[instructionIndex];
                        if (instruction.Operand is IResolutionScope resolutionScope)
                        {
                            var replacedScope = TryReplaceScope(resolutionScope);
                            if (replacedScope is not null)
                                instruction.Operand = replacedScope;
                        }
                        else if (instruction.Operand is TypeReference typeReference)
                        {
                            var replacedScope = TryReplaceScope(typeReference.Scope);
                            if (replacedScope is not null)
                                typeReference.Scope = replacedScope;
                        }
                        else if (instruction.Operand is MemberReference memberReference)
                        {
                            if (memberReference.Parent is TypeReference memberTypeReference)
                            {
                                var replacedScope = TryReplaceScope(memberTypeReference.Scope);
                                if (replacedScope is not null)
                                    memberTypeReference.Scope = replacedScope;
                            }
                            else if (memberReference.Parent is ModuleReference moduleReference)
                            {
                                var replacedScope = TryReplaceScope(moduleReference);
                                if (replacedScope is IMemberRefParent memberRefParent)
                                    memberReference.Parent = memberRefParent;
                            }
                        }
                    }

                    IResolutionScope TryReplaceScope(IResolutionScope? scope)
                    {
                        if (scope is AssemblyReference assemblyReference)
                        {
                            for (var referenceIndex = 0; referenceIndex < modules.Count; referenceIndex = referenceIndex + 1)
                            {
                                var referenceModule = modules[referenceIndex];
                                var referenceAssembly = referenceModule.Assembly;
                                if (assemblyReference.Name == referenceAssembly!.Name)
                                    return Module;
                            }
                        }

                        if (scope is ModuleReference moduleReference)
                        {
                            for (var referenceIndex = 0; referenceIndex < modules.Count; referenceIndex = referenceIndex + 1)
                            {
                                var referenceModule = modules[referenceIndex];
                                if (moduleReference.Name == referenceModule.Name)
                                    return Module;
                            }
                        }

                        return null;
                    }
                }
            }
        }

        void SolveModulesConflicts()
        {
            var moduleTypes = Module.TopLevelTypes.Where(type => type.Name == "<Module>" && type.MetadataToken != 0x02000001).ToArray();

            var methods = moduleTypes
                .Select(method => method.GetStaticConstructor())
                .Where(method => method is not null);

            var cctor = Module.GetOrCreateModuleConstructor();
            var existingBody = cctor.CilMethodBody;
            CilMethodBody body;
            if (existingBody is null)
            {
                body = cctor.CilMethodBody = new CilMethodBody();
            }
            else
            {
                body = existingBody;
            }

            var instructions = body.Instructions;
            var exceptionHandlers = body.ExceptionHandlers;
            var localVariables = body.LocalVariables;

            CilInstructionLabel continuationLabel = null;
            if (existingBody is not null && instructions.Count > 0)
            {
                var existingTryInstructions = new HashSet<CilInstruction>();
                foreach (var handler in exceptionHandlers)
                {
                    if (handler.TryStart is null || handler.TryEnd is null)
                        continue;
                    var start = ((CilInstructionLabel)handler.TryStart).Instruction;
                    var end = ((CilInstructionLabel)handler.TryEnd).Instruction;
                    if (start is null || end is null)
                        continue;
                    var inTry = false;
                    foreach (var instruction in instructions)
                    {
                        if (instruction == start)
                            inTry = true;
                        if (inTry)
                            existingTryInstructions.Add(instruction);
                        if (instruction == end)
                            break;
                    }
                }

                var continuationNop = new CilInstruction(CilOpCodes.Nop);
                continuationLabel = new CilInstructionLabel();
                continuationLabel.Instruction = continuationNop;

                foreach (var instruction in instructions)
                {
                    if (instruction.OpCode.Code != CilCode.Ret)
                        continue;
                    if (existingTryInstructions.Contains(instruction))
                    {
                        instruction.OpCode = CilOpCodes.Leave;
                        instruction.Operand = continuationLabel;
                    }
                    else
                    {
                        instruction.OpCode = CilOpCodes.Br;
                        instruction.Operand = continuationLabel;
                    }
                }

                instructions.Add(continuationNop);
            }

            foreach (var method in methods)
            {
                var methodBody = method.CilMethodBody;
                if (methodBody is null)
                    continue;

                var localMap = new Dictionary<CilLocalVariable, CilLocalVariable>();
                foreach (var originalLocal in methodBody.LocalVariables)
                {
                    var newLocal = new CilLocalVariable(originalLocal.VariableType);
                    localVariables.Add(newLocal);
                    localMap[originalLocal] = newLocal;
                }

                var originalLabels = new HashSet<ICilLabel>();
                foreach (var instruction in methodBody.Instructions)
                {
                    if (instruction.Operand is CilInstructionLabel label)
                        originalLabels.Add(label);
                    else if (instruction.Operand is IList<CilInstructionLabel> labelList)
                        foreach (var lalabel in labelList)
                            originalLabels.Add(lalabel);
                }

                foreach (var handler in methodBody.ExceptionHandlers)
                {
                    if (handler.TryStart is not null) originalLabels.Add(handler.TryStart);
                    if (handler.TryEnd is not null) originalLabels.Add(handler.TryEnd);
                    if (handler.HandlerStart is not null) originalLabels.Add(handler.HandlerStart);
                    if (handler.HandlerEnd is not null) originalLabels.Add(handler.HandlerEnd);
                    if (handler.FilterStart is not null) originalLabels.Add(handler.FilterStart);
                }

                var instructionMap = new Dictionary<CilInstruction, CilInstruction>();
                var clonedInstructions = new List<CilInstruction>();
                foreach (var originalInstruction in methodBody.Instructions)
                {
                    var clone = new CilInstruction(originalInstruction.OpCode, originalInstruction.Operand);
                    clonedInstructions.Add(clone);
                    instructionMap[originalInstruction] = clone;
                }

                var labelMap = new Dictionary<ICilLabel, CilInstructionLabel>();
                foreach (var originalLabel in originalLabels)
                {
                    var targetOriginalInstruction = ((CilInstructionLabel)originalLabel).Instruction;
                    if (targetOriginalInstruction is null)
                        continue;
                    if (!instructionMap.TryGetValue(targetOriginalInstruction, out var newInstruction))
                        continue;
                    var newLabel = new CilInstructionLabel();
                    newLabel.Instruction = newInstruction;
                    labelMap[originalLabel] = newLabel;
                }

                foreach (var clone in clonedInstructions)
                {
                    if (clone.Operand is ICilLabel originalLabel && labelMap.TryGetValue(originalLabel, out var mappedLabel))
                        clone.Operand = mappedLabel;
                    else if (clone.Operand is IList<ICilLabel> labelList)
                    {
                        var mappedList = new ICilLabel[labelList.Count];
                        for (int index = 0; index < labelList.Count; index++)
                        {
                            var item = labelList[index];
                            mappedList[index] = labelMap.TryGetValue(item, out var mapped) ? mapped : item;
                        }
                        clone.Operand = mappedList;
                    }
                    else if (clone.Operand is CilLocalVariable originalLocal && localMap.TryGetValue(originalLocal, out var newLocal))
                        clone.Operand = newLocal;
                }

                var tryBlockInstructions = new HashSet<CilInstruction>();
                foreach (var handler in methodBody.ExceptionHandlers)
                {
                    if (handler.TryStart is null || handler.TryEnd is null)
                        continue;
                    var startInstruction = ((CilInstructionLabel)handler.TryStart).Instruction;
                    var endInstruction = ((CilInstructionLabel)handler.TryEnd).Instruction;
                    if (startInstruction is null || endInstruction is null)
                        continue;
                    var inTry = false;
                    foreach (var instruction in methodBody.Instructions)
                    {
                        if (instruction == startInstruction)
                            inTry = true;
                        if (inTry)
                            tryBlockInstructions.Add(instruction);
                        if (instruction == endInstruction)
                            break;
                    }
                }

                var afterMethodNop = new CilInstruction(CilOpCodes.Nop);
                var afterMethodLabel = new CilInstructionLabel();
                afterMethodLabel.Instruction = afterMethodNop;

                foreach (var originalInstruction in methodBody.Instructions)
                {
                    if (originalInstruction.OpCode.Code != CilCode.Ret)
                        continue;
                    if (!instructionMap.TryGetValue(originalInstruction, out var clone))
                        continue;
                    if (tryBlockInstructions.Contains(originalInstruction))
                    {
                        clone.OpCode = CilOpCodes.Leave;
                        clone.Operand = afterMethodLabel;
                    }
                    else
                    {
                        clone.OpCode = CilOpCodes.Br;
                        clone.Operand = afterMethodLabel;
                    }
                }

                foreach (var clone in clonedInstructions)
                    instructions.Add(clone);

                instructions.Add(afterMethodNop);

                foreach (var originalHandler in methodBody.ExceptionHandlers)
                {
                    var newHandler = new CilExceptionHandler();
                    newHandler.HandlerType = originalHandler.HandlerType;
                    if (originalHandler.ExceptionType is not null)
                        newHandler.ExceptionType = originalHandler.ExceptionType;
                    newHandler.TryStart = originalHandler.TryStart is not null && labelMap.TryGetValue(originalHandler.TryStart, out var tryStart) ? tryStart : null;
                    newHandler.TryEnd = originalHandler.TryEnd is not null && labelMap.TryGetValue(originalHandler.TryEnd, out var tryEnd) ? tryEnd : null;
                    newHandler.HandlerStart = originalHandler.HandlerStart is not null && labelMap.TryGetValue(originalHandler.HandlerStart, out var handlerStart) ? handlerStart : null;
                    newHandler.HandlerEnd = originalHandler.HandlerEnd is not null && labelMap.TryGetValue(originalHandler.HandlerEnd, out var handlerEnd) ? handlerEnd : null;
                    newHandler.FilterStart = originalHandler.FilterStart is not null && labelMap.TryGetValue(originalHandler.FilterStart, out var filterStart) ? filterStart : null;
                    exceptionHandlers.Add(newHandler);
                }
            }

            instructions.Add(new CilInstruction(CilOpCodes.Ret));

            foreach (var type in moduleTypes)
                Module.TopLevelTypes.Remove(type);
        }
    }
}