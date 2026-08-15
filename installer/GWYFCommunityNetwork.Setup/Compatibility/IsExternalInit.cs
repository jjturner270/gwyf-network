// net48 lacks System.Runtime.CompilerServices.IsExternalInit, which newer C#
// requires for `init` accessors. This shim is the standard workaround; it is
// internal and never leaks out of this assembly.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}