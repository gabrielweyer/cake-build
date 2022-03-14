using Newtonsoft.Json;

namespace Contoso.Hello.Logic;

public static class ReallyUseless
{
    public static string SayHi()
    {
        return JsonConvert.SerializeObject("Hello");
    }
}
