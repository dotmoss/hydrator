namespace Hydrator;

public static class NamespaceRemover
{
    public static void RemoveNamespaces()
    {
        var existingRootTypes = new HashSet<string>();
        foreach (var type in Module.TopLevelTypes)
        {
            if (type.Namespace is null)
                existingRootTypes.Add(type.Name);
        }

        foreach (var type in Module.TopLevelTypes)
        {
            if (type.Namespace is null)
                continue;

            if (Options.IsLibrary && type.IsPublic)
                continue;

            var currentName = type.Name;
            var counter = 1;
            while (existingRootTypes.Contains(currentName))
            {
                currentName = type.Name + counter;
                counter = counter + 1;
            }

            type.Namespace = null;
            type.Name = currentName;
            existingRootTypes.Add(currentName);
        }
    }
}