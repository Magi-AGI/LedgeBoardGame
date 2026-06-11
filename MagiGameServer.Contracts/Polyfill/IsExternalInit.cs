// netstandard2.1 does not ship System.Runtime.CompilerServices.IsExternalInit,
// which the C# 9 compiler requires to emit init-only setters and positional
// records. Defining it as an empty internal type in each contracts consumer
// is the standard polyfill — C# only looks for the type's existence, not its
// contents, so this unlocks records without pulling in a newer TFM.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
