using System.Collections.Generic;

namespace Diagrid.Aspire.Hosting.Catalyst;

public interface CatalystComponent
{
}

public interface CatalystComponent<MetadataType> : CatalystComponent
{
    public string Type { get; }

    public IList<string> Scopes { get; }

    public MetadataType Metadata { get; }
}
