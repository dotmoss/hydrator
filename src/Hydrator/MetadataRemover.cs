using AsmResolver.DotNet;

namespace Hydrator;

public static class MetadataRemover
{
    public static void RemoveNullableAttributes()
    {
        foreach (var typeDefinition in Module.TopLevelTypes)
        {
            RemoveAttributeFromType(typeDefinition, "System.Runtime.CompilerServices.NullableAttribute");
            RemoveAttributeFromType(typeDefinition, "System.Runtime.CompilerServices.NullableContextAttribute");
        }
    }

    static void RemoveAttributeFromType(TypeDefinition typeDefinition, string attributeName)
    {
        RemoveFromCollection(typeDefinition.CustomAttributes, attributeName);

        foreach (var genericParameter in typeDefinition.GenericParameters)
            RemoveFromCollection(genericParameter.CustomAttributes, attributeName);

        foreach (var fieldDefinition in typeDefinition.Fields)
            RemoveFromCollection(fieldDefinition.CustomAttributes, attributeName);

        foreach (var propertyDefinition in typeDefinition.Properties)
            RemoveFromCollection(propertyDefinition.CustomAttributes, attributeName);

        foreach (var eventDefinition in typeDefinition.Events)
            RemoveFromCollection(eventDefinition.CustomAttributes, attributeName);

        foreach (var methodDefinition in typeDefinition.Methods)
            ProcessMethod(methodDefinition, attributeName);

        foreach (var nestedType in typeDefinition.NestedTypes)
            RemoveAttributeFromType(nestedType, attributeName);

        static void ProcessMethod(MethodDefinition methodDefinition, string attributeName)
        {
            RemoveFromCollection(methodDefinition.CustomAttributes, attributeName);

            foreach (var genericParameter in methodDefinition.GenericParameters)
                RemoveFromCollection(genericParameter.CustomAttributes, attributeName);

            foreach (var parameterDefinition in methodDefinition.ParameterDefinitions)
                RemoveFromCollection(parameterDefinition.CustomAttributes, attributeName);
        }

        static void RemoveFromCollection(IList<CustomAttribute> attributes, string attributeName)
        {
            for (var attributeIndex = attributes.Count - 1; attributeIndex >= 0; attributeIndex--)
            {
                var attribute = attributes[attributeIndex];
                var attributeType = attribute.Constructor?.DeclaringType;
                if (attributeType is not null && attributeType.FullName == attributeName)
                    attributes.RemoveAt(attributeIndex);
            }
        }
    }

    public static void RemoveAssemblyMetadataAttribute()
    {
        var assembly = Module.Assembly!;

        assembly.CustomAttributes
            .Where(attribute => (attribute.Type.Name.ToString() is "AssemblyVersionAttribute" or "DebuggableAttribute" && Options.IsApplication)||
                attribute.Type.Name.ToString()
                is "AssemblyCompanyAttribute" 
                or "AssemblyConfigurationAttribute"
                or "AssemblyFileVersionAttribute"
                or "AssemblyInformationalVersionAttribute"
                or "AssemblyProductAttribute"
                or "AssemblyTitleAttribute")
            .ToList()
            .ForEach(attribute => assembly.CustomAttributes.Remove(attribute));

        if (Options.IsApplication)
        {
            var debuggableAttribute = Module.CustomAttributes.FirstOrDefault(attribute => attribute.Type!.Name!.ToString() is "RefSafetyRulesAttribute");
            if (debuggableAttribute is not null)
                Module.CustomAttributes.Remove(debuggableAttribute);
        }
    }
}