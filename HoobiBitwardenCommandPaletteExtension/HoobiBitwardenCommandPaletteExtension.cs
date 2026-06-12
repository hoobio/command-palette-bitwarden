// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace HoobiBitwardenCommandPaletteExtension;

// COM activation CLSID, one per side-by-side channel so Dev/Prerelease/Release can run
// concurrently without fighting over the same class id. Must match the CLSID stamped into
// Package.appxmanifest by the build (driven by PackageChannel in the .csproj).
#if CHANNEL_DEV
[Guid("ef6edc31-9567-4d38-8463-4a59b038148d")]
#elif CHANNEL_PRERELEASE
[Guid("e874e604-b64e-42f1-b391-756d4d406c51")]
#else
[Guid("8345c10d-0df8-4116-a767-3f8647adf4df")]
#endif
public sealed partial class HoobiBitwardenCommandPaletteExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly HoobiBitwardenCommandPaletteExtensionCommandsProvider _provider = new();

    public HoobiBitwardenCommandPaletteExtension(ManualResetEvent extensionDisposedEvent)
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

    public void Dispose() => this._extensionDisposedEvent.Set();
}
