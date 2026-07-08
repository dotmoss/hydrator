global using static Hydrator.Globals;
using AsmResolver;
using AsmResolver.DotNet;
using System.Text.Json;

namespace Hydrator;

static class Globals
{
    public static ProgramOptions Options;
    public static ModuleDefinition Module;
    public static RuntimeContext Context;
}

static class Program
{
    static DotNetRuntimeInfo ExtractDotnetRuntimeInfo(string path)
    {
        return ModuleDefinition.FromFile(path).RuntimeContext!.TargetRuntime;
    }

    static void Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Wrong program arguments. Enable test environment");
            args = 
            [
                @"..\..\..\..\..\Target1\bin\x64\Release\net10.0\Target1.dll",
                @"..\..\..\..\..\Target1\bin\x64\Release\net10.0\Target1_hydrated.dll",
                JsonSerializer.Serialize(new ProgramOptions())
            ];
        }        

        var (inputPath, outputPath, stringOptions) = (args[0], args[1], args[2]);
        (inputPath, outputPath) = (Path.GetFullPath(inputPath), Path.GetFullPath(outputPath));

        Options = JsonSerializer.Deserialize<ProgramOptions>(stringOptions)!;
        Options.TargetDirectory = Path.GetDirectoryName(inputPath)!;

        var dotnetRuntimeInfo = ExtractDotnetRuntimeInfo(inputPath);
        var context = new RuntimeContext(dotnetRuntimeInfo);
        var assembly = context.LoadAssembly(inputPath);
        Module = assembly.ManifestModule!;
        Options.IsApplication = Module.ManagedEntryPointMethod is not null;
        Context = Module.RuntimeContext!;

        Execute();

        Module.Write(outputPath);
        Console.ReadLine();
    }

    static void Execute()
    {
        if (Options.MergeModules)
        {
            ModuleMerger.MergeModules(GetModulesForMerge());
        }

        ModuleUtils.FlattenTypes();
        MetadataRemover.RemoveNullableAttributes();
        MetadataRemover.RemoveAssemblyMetadataAttribute();
        NamespaceRemover.RemoveNamespaces();
        NameReducer.ReduceNames();

        VisibilityUtils.SolveVisibilityConflicts();
        //var genericParameters = TypeResolver.ResolveAllGenericParameters(module);
        //RemoveEmptyTypes(module, genericParameters);

        List<ModuleDefinition> GetModulesForMerge()
        {
            var modules = new List<ModuleDefinition>();
            GetReferences(Module, modules);

            return modules;

            void GetReferences(ModuleDefinition module, List<ModuleDefinition> references)
            {
                foreach (var reference in Module.AssemblyReferences)
                {
                    try
                    {
                        if (reference.Resolve(Module.RuntimeContext) is { } assembly)
                        {
                            foreach (var assemblyModule in assembly.Modules)
                            {
                                if (assemblyModule.FilePath is { } assemblyPath && !assemblyPath.Contains(@"dotnet\shared"))
                                {
                                    if (references.Contains(assemblyModule))
                                        continue;

                                    modules.Add(assemblyModule);
                                    GetReferences(assemblyModule, references);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }
    }

    static void RemoveEmptyTypes(ModuleDefinition module, List<TypeDefinition> ignoredTypes)
    {
    }
}