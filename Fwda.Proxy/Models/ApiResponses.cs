namespace Fwda.Proxy.Models;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
internal sealed record RootResponse(string Service, string Version, string Status);

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
internal sealed record HealthResponse(string Status, int Portals, IReadOnlyList<string> PortalNames);
