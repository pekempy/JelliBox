// Polyfill: C# 9+ init-only setters and records require System.Runtime.CompilerServices.IsExternalInit.
// The UWP BCL (Microsoft.NETCore.UniversalWindowsPlatform) targets a pre-.NET-5 runtime that
// doesn't ship this type. Declaring it here satisfies the compiler without affecting runtime.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
