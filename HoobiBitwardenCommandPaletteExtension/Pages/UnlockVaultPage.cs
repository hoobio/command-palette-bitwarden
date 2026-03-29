using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class UnlockVaultPage : ContentPage
{
  private readonly UnlockForm _form;

  public UnlockVaultPage(BitwardenCliService service, BitwardenSettingsManager? settings = null, Action<string>? onSubmit = null, Action? onBiometricUnlock = null)
  {
    Name = "Unlock";
    Title = "Unlock Bitwarden Vault";
    Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    _form = new UnlockForm(service, settings, onSubmit, onBiometricUnlock);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class UnlockForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly BitwardenSettingsManager? _settings;
  private readonly Action<string>? _onSubmit;
  private readonly Action? _onBiometricUnlock;

  private string BuildFormTemplate()
  {
    var rememberChecked = _settings?.RememberSession.Value == true;
    var showWindowsHello = _settings?.UseDesktopIntegration.Value == true;
    var windowsHelloAction = showWindowsHello ? """
                    ,
                    {
                        "type": "Action.Submit",
                        "title": "Windows Hello",
                        "iconUrl": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAABHNCSVQICAgIfAhkiAAAA8ZJREFUaIHtWO1x2zAMBTuBNig3qDaIOkG8QdUJ4k4QbxBvYG8QdwK7EziZwM4EVid4/SGoeQJJWZYUX+9q3OnOBkHy4ZMgRW50o/+b3NQLAshE5Etk6M05d5x6v9EKKOB7EZmJSC4i/syUo4js9PvpnKvGYhhEADyAFcbTCkAxFMcgDwCYichzh8iriFjLehH53DFnIyLfr+IRABtjxRcAcwB5j7m5yu4i3jj1WWMKBXIFvRnjfg3DtVFiNx3S941yAFsAq4Hz7/TzifECwFEVKMdgjS2eq2sbKjpArDQ/mG8tfFK5+0mB9gRfWSsq8K0BmdG4zRemg1V4SvAZgL0BnxuZpwioysh4AEvUSXuMyEMN0DLMFAosU+AV1D4CZM3WT6zbKFSZuSeMKAixTZhKGrNhBdRh4i/cIzNGauhxCgUKWnB3Bvw8Mt8DuAfwSN8DgLuIbI4wtMoplJirhTL9n6FOuoZiOfEN8dBiaqqQp3kZwoNtvBIG3CoFXq14DnhMkQezx4uRKaYCPzMLz2isRBhWUIsu6FsjXoWe0fYyK3GYSoEDLbox4JkqBZusRKjzy1p6T+MZ2hVqMRY8J3RF1oodcr0bMYQn9IrG5sQ/dRmkz0bbmDVw/pDLUCf1o37fLBD1FlNBY5zUQaXrCz43G3jl29DhhM7QfdF5QrrVKInPewzLBbRdybHP1l8b8LGEtrRHO3GXiMQ62rlQDFEgsA7ap3SruUPY1G3wXoVs4q4T2/L+fFIHCvZRgK3plZfyig23IKHN3L9rduzP5Xt3KfiMLU189sqC+Gytdce6PH9pxh5Ahxva3j5dqkCqH+LqUJzjR9Zlqx6Iz96ZE5/zIPDqp4u0qokbspcuvlrwBLqsO+c2JOcRr/EMlOWDF4suBTz9PsYEUk8gxC9FJNOPb16/6HcDlo3BL3tzEfkhIl9jL3tdCrCw75DrIgaVJ/gF8X43sk0YOucq59zSObeLbTAkhN6aH7GYNMQe4lAJFFOvcbisEuHVj3omcUl8rvOF8lo3O5K1Nz5PfE7aE85c+pMeUJe96l+2TJ+waKx6lLbHCuJzHjwRvyR+JnUODCfrRrR7lF2Cz+1x9HwwHrbebFru6pwHhiiUcn+W4DPQVmuMsKX++HdR3ZjjfUF8PmXnPeTtDexqj7t8avJpGm2BEbbf3ijBV82lfDQhvPbNuvg6xtVrb9bjp5WPeWqMKMHJyeHCt6ytAcm0uArQFKm116jjnhPTJjnf1qKt+D9HaFeXwowtVOnrVJwhhI5r4o1udKNp6Q+CBIHlcdToFgAAAABJRU5ErkJggg==",
                        "data": { "action": "biometric" }
                    }
""" : "";
    return $$"""
    {
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "type": "AdaptiveCard",
        "version": "1.6",
        "body": [
            {
                "type": "TextBlock",
                "size": "medium",
                "weight": "bolder",
                "text": "Unlock your Bitwarden vault",
                "horizontalAlignment": "center",
                "wrap": true,
                "style": "heading"
            },
            {
                "type": "Input.Text",
                "label": "Master Password",
                "style": "Password",
                "id": "MasterPassword",
                "placeholder": "Enter your master password"
            },
            {
                "type": "ActionSet",
                "actions": [
                    {
                        "type": "Action.Submit",
                        "title": "Unlock",
                        "data": { "action": "password" }
                    }{{windowsHelloAction}}
                ]
            },
            {
                "type": "Input.Toggle",
                "id": "RememberSession",
                "title": "Remember session (stay unlocked between launches)",
                "valueOn": "true",
                "valueOff": "false",
                "value": "{{(rememberChecked ? "true" : "false")}}"
            },
            {
                "type": "TextBlock",
                "text": "[Upvote this issue](https://github.com/microsoft/PowerToys/issues/46003) to help bring Enter key support.",
                "wrap": true,
                "isSubtle": true,
                "size": "small"
            }
        ]
    }
    """;
  }

  public UnlockForm(BitwardenCliService service, BitwardenSettingsManager? settings = null, Action<string>? onSubmit = null, Action? onBiometricUnlock = null)
  {
    _service = service;
    _settings = settings;
    _onSubmit = onSubmit;
    _onBiometricUnlock = onBiometricUnlock;
    TemplateJson = BuildFormTemplate();
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var actionData = JsonNode.Parse(data)?.AsObject();
    var action = actionData?["action"]?.GetValue<string>();

    var remember = formInput?["RememberSession"]?.GetValue<string>() == "true";
    if (_settings != null && _settings.RememberSession.Value != remember)
    {
      _settings.RememberSession.Value = remember;
      _settings.SaveSettings();
    }

    if (action == "biometric")
    {
      _onBiometricUnlock?.Invoke();
      return CommandResult.KeepOpen();
    }

    var password = formInput?["MasterPassword"]?.GetValue<string>();
    if (string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    _onSubmit?.Invoke(password);
    return CommandResult.GoBack();
  }
}
