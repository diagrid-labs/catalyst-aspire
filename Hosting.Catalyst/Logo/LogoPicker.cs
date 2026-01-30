using System;
using System.IO;
using System.Linq;

namespace Diagrid.Aspire.Hosting.Catalyst.Logo;

internal static class LogoPicker
{
    public static string PickRandomLogo()
    {
        var assembly = typeof(LogoPicker).Assembly;

        var logos = assembly.GetManifestResourceNames()
            .Where(name =>
                name.Contains("Diagrid.Aspire.Hosting.Catalyst.Logo.")
                && name.EndsWith(".txt"))
            .ToList();

        var randomInt = Random.Shared.Next(logos.Count - 1);
        var logoResourceName = logos[randomInt];

        var logoStream = assembly.GetManifestResourceStream(logoResourceName);

        return logoStream is not null
            ? new StreamReader(logoStream).ReadToEnd()
            : "C a t a l y s t";
    }
}
