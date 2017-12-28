using Contoso.Hello.Logic;
using ExtraLogic;

namespace Contoso.Hello.SuperLogic
{
    public static class EvenMoreUseless
    {
        public static string DoWork()
        {
            return IncrediblyUsless.DoNothing($"Some JSON (maybe): {ReallyUseless.SayHi()}");
        }
    }
}