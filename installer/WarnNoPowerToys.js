// Single-OK warning shown when PowerToys / PowerToys Preview isn't detected.
// MSI Session.Message returns control to the installer immediately, so the
// install proceeds whether the user dismisses the dialog or not. In quiet
// mode the message is logged rather than displayed.
//
// Referenced from HoobiBitwardenCommandPaletteExtension.wxs as
// <CustomAction ... ScriptSourceFile="WarnNoPowerToys.js" />

var INSTALLMESSAGE_WARNING = 0x03000000;
var MB_OK = 0;
var MB_ICONWARNING = 0x30;

var rec = Session.Installer.CreateRecord(0);
rec.StringData(0) = "PowerToys was not detected on this system.\n\n" +
    "The Bitwarden Command Palette extension runs inside the Microsoft " +
    "Command Palette, which ships with PowerToys (or PowerToys Preview).\n\n" +
    "Installation will continue, but the extension will not be discoverable " +
    "until PowerToys is installed. See https://aka.ms/installpowertoys.";
Session.Message(INSTALLMESSAGE_WARNING + MB_ICONWARNING + MB_OK, rec);
