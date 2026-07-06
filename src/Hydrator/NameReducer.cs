namespace Hydrator;

public static class NameReducer
{
    public static void ReduceNames()
    {
        foreach (var type in Module.GetAllTypes())
        {
            var isTypeVisible = Options.IsLibrary && type.IsPublic;

            if (!isTypeVisible)
                type.Name = null;

            foreach (var field in type.Fields)
                if (!(Options.IsLibrary && (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly)))
                    field.Name = null;

            foreach (var property in type.Properties)
            {
                var isGetterVisible = property.GetMethod != null && (property.GetMethod.IsPublic || property.GetMethod.IsFamily || property.GetMethod.IsFamilyOrAssembly);
                var isSetterVisible = property.SetMethod != null && (property.SetMethod.IsPublic || property.SetMethod.IsFamily || property.SetMethod.IsFamilyOrAssembly);
                var isPropertyVisible = Options.IsLibrary && (isGetterVisible || isSetterVisible);

                if (!isPropertyVisible)
                    property.Name = null;
            }

            foreach (var method in type.Methods)
            {
                if (method.IsConstructor)
                    continue;

                var isMethodVisible = Options.IsLibrary && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

                if (!isMethodVisible)
                {
                    method.Name = null;
                    foreach (var parameter in method.Parameters)
                        parameter.Definition!.Name = null;
                }
            }

            foreach (var @event in type.Events)
            {
                var isAddVisible = @event.AddMethod != null && (@event.AddMethod.IsPublic || @event.AddMethod.IsFamily || @event.AddMethod.IsFamilyOrAssembly);
                var isRemoveVisible = @event.RemoveMethod != null && (@event.RemoveMethod.IsPublic || @event.RemoveMethod.IsFamily || @event.RemoveMethod.IsFamilyOrAssembly);
                var isEventVisible = Options.IsLibrary && (isAddVisible || isRemoveVisible);

                if (!isEventVisible)
                {
                    @event.Name = null;
                }
            }
        }
    }
}