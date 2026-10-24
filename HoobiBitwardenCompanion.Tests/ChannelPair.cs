using System.IO.Pipes;
using System.Text.Json;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.Tests;

internal sealed class ChannelPair : IDisposable
{
  public ChannelPair(IpcRequestHandler extensionHandler, IpcRequestHandler? companionHandler = null)
  {
    var toExtension = new AnonymousPipeServerStream(PipeDirection.Out);
    var fromExtension = new AnonymousPipeServerStream(PipeDirection.In);
    CompanionInput = fromExtension;
    CompanionOutput = toExtension;
    Extension = new PipeChannel(
      new AnonymousPipeClientStream(PipeDirection.In, toExtension.ClientSafePipeHandle),
      new AnonymousPipeClientStream(PipeDirection.Out, fromExtension.ClientSafePipeHandle),
      extensionHandler);
    _ = Task.Run(Extension.RunAsync);
    if (companionHandler is not null)
    {
      Companion = new PipeChannel(fromExtension, toExtension, companionHandler);
      CompanionLoop = Task.Run(Companion.RunAsync);
    }
  }

  public Stream CompanionInput { get; }

  public Stream CompanionOutput { get; }

  public PipeChannel Extension { get; }

  public PipeChannel? Companion { get; }

  public Task CompanionLoop { get; } = Task.CompletedTask;

  public void Dispose()
  {
    Extension.Dispose();
    Companion?.Dispose();
  }

  public static JsonElement? Reply<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) => IpcJson.ToElement(value, info);
}
