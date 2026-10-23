using System.Reflection;

namespace SdqTerminal;

public static class AppVersion
{
    // The SDK generates assembly metadata from <Version> in SdqTerminal.csproj.
    public static string Display { get; } = typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];
}
