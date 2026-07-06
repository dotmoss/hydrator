using AsmResolver;
using AsmResolver.DotNet;

namespace Hydrator;

public static class NameUtils
{
    public static Utf8String? GetClearTypeName(TypeDefinition type)
    {
        var name = type.Name;
        if (name is not null)
        {
            var genericIndex = name.IndexOf('`');
            if (genericIndex != -1)
                return new Utf8String(new string(name.Take(genericIndex).ToArray()));
        }
        
        return name;
    }
}