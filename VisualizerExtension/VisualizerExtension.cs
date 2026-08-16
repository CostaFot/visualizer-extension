using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace VisualizerExtension;

[Guid("50051ad1-efd9-485e-bdd9-8f09ae9ea05a")]
public sealed partial class VisualizerExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly VisualizerCommandsProvider _provider = new();

    public VisualizerExtension(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose()
    {
        this._provider.Dispose();
        this._extensionDisposedEvent.Set();
    }
}
