using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Tests;

internal static class RepromptDriver
{
  public static void Attach(ItemViewModel vm, Func<string?> password, Action<string?> failure) =>
    vm.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(ItemViewModel.IsPrompting) && vm.IsPrompting)
        _ = AnswerAsync(vm, password(), failure);
    };

  private static async Task AnswerAsync(ItemViewModel vm, string? password, Action<string?> failure)
  {
    await vm.SubmitPasswordAsync(password);
    failure(vm.PasswordError);
    if (vm.IsPrompting)
      vm.CancelPrompt();
  }
}
