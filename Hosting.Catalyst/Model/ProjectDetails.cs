using System;

namespace Diagrid.Aspire.Hosting.Catalyst.Model;

public record ProjectDetails
{
    public required Uri HttpEndpoint { get; init; }

    public required Uri GrpcEndpoint { get; init; }
}