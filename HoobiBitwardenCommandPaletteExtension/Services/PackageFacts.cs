using Windows.ApplicationModel;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class PackageFacts
{
  internal static (string Version, string Name, bool IsStore) Read()
  {
    var package = Package.Current;
    var version = package.Id.Version;
    return ($"{version.Major}.{version.Minor}.{version.Build}", package.Id.Name, package.SignatureKind == PackageSignatureKind.Store);
  }
}
