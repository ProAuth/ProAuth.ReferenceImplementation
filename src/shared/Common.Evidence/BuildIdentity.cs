using System.Reflection;
namespace Common.Evidence;

public static class BuildIdentity
{
    public static string ProAuthPackageVersion => typeof(BuildIdentity).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "ProAuthPackageVersion").Value!;
}
