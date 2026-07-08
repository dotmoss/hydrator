using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Hydrator;

public unsafe static class NameReducer
{
    public static void ReduceNames()
    {
        foreach (var type in Module.GetAllTypes())
        {
            var takenMethodSignatures = new List<MemberSignature>();
            var takenFieldsSignatures = new List<MemberSignature>();

            if (!(Options.IsLibrary && type.IsPublic))
                type.Name = Utf8String.Empty;

            foreach (var field in type.Fields)
                if (!(Options.IsLibrary && (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly)))
                    MemberRenamer.RenameField(field, GetNewName(takenFieldsSignatures, field.Signature));

            foreach (var method in type.Methods)
            {
                if (method.IsConstructor)
                    continue;

                if (!(Options.IsLibrary && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly)))
                {
                    MemberRenamer.RenameMethod(method, GetNewName(takenMethodSignatures, method.Signature));
                    foreach (var parameter in method.Parameters)
                        parameter.Definition!.Name = Utf8String.Empty;
                }
            }

            foreach (var property in type.Properties)
            {
                var isGetterVisible = property.GetMethod is not null && (property.GetMethod.IsPublic || property.GetMethod.IsFamily || property.GetMethod.IsFamilyOrAssembly);
                var isSetterVisible = property.SetMethod is not null && (property.SetMethod.IsPublic || property.SetMethod.IsFamily || property.SetMethod.IsFamilyOrAssembly);

                if (!(Options.IsLibrary && (isGetterVisible || isSetterVisible)))
                    property.Name = Utf8String.Empty;
            }

            foreach (var @event in type.Events)
            {
                var isAddVisible = @event.AddMethod != null && (@event.AddMethod.IsPublic || @event.AddMethod.IsFamily || @event.AddMethod.IsFamilyOrAssembly);
                var isRemoveVisible = @event.RemoveMethod != null && (@event.RemoveMethod.IsPublic || @event.RemoveMethod.IsFamily || @event.RemoveMethod.IsFamilyOrAssembly);
                if (!(Options.IsLibrary && (isAddVisible || isRemoveVisible)))
                    @event.Name = Utf8String.Empty;
            }

            Utf8String GetNewName(List<MemberSignature> takenSignatures, MemberSignature? signature)
            {
                if (!type.HasGenericParameters)
                    return Utf8String.Empty;

                var sameSignatureFields = takenSignatures.Where(takenSignature => SignatureComparer.Default.Equals(takenSignature, signature)).ToArray();
                takenSignatures.Add(signature!);
                var ordinal = (uint)sameSignatureFields.Length;
                if (ordinal == 0)
                    return Utf8String.Empty;

                return new Utf8String(new Span<byte>(ref Unsafe.As<uint, byte>(ref ordinal)).Slice(0, (BitOperations.Log2(ordinal) + 8) / 8));
            }
        }
    }
}