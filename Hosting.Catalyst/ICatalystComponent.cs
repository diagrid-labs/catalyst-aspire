using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst;

public interface ICatalystComponent
{
}

public interface ICatalystComponent<TMetadata> : ICatalystComponent
{
    public string Type { get; }

    public IList<string> Scopes { get; }

    public TMetadata Metadata { get; }
}
