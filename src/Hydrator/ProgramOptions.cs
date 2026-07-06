using System.Text.Json.Serialization;

namespace Hydrator;

public class ProgramOptions
{
    public bool MergeModules = true;
    public bool ConcretizeTargetFramework = true;

    [JsonIgnore]
    public bool IsApplication;

    [JsonIgnore]
    public bool IsLibrary => !IsApplication;

    [JsonIgnore]
    public string TargetDirectory;
}