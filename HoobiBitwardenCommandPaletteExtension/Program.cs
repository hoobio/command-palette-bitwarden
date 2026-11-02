// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        var hardeningError = ProcessHardening.Apply();
        if (hardeningError != 0)
        {
            DebugLogService.Log("Security", $"Process hardening failed (Win32 error {hardeningError})");
        }

        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);
            
            HoobiBitwardenCommandPaletteExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<HoobiBitwardenCommandPaletteExtension, IExtension>(() => extensionInstance);
            server.Start();
            
            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as a Extension... exiting.");
        }
    }
}
