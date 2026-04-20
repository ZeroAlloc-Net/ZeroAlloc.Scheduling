// Polyfill required for records with init-only properties on netstandard2.0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
