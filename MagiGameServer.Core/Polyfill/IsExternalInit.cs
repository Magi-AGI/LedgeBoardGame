// Polyfill so C# 9 `init` accessors and records compile on netstandard2.1.
// The compiler only requires the type to exist at the expected name — the
// runtime has no hard dependency on it. Internal so we don't pollute the
// public surface of downstream consumers.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
