using Avalonia.Controls;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class ItemEditView : UserControl
{
  public ItemEditView() => InitializeComponent();

  internal Control? FindEditor(string key)
  {
    if (DataContext is not ItemEditorViewModel editor)
      return null;
    if (TryIndex(key, ItemFields.UriPrefix, out var uri))
      return FirstEditor(UriList, uri, 0);
    if (TryIndex(key, ItemFields.CustomPrefix, out var custom))
      return key == editor.SshHostKey ? SshHostBox : FirstEditor(FieldList, editor.CustomEditorIndex(custom), 1);
    return this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsEffectivelyVisible && c.Tag as string == key);
  }

  private static bool TryIndex(string key, string prefix, out int index)
  {
    index = -1;
    return key.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(key.AsSpan(prefix.Length), out index);
  }

  private static Control? FirstEditor(ItemsControl list, int index, int skip) =>
    list.ContainerFromIndex(index)?.GetVisualDescendants().OfType<Control>().Where(c => c is TextBox or ToggleSwitch or ComboBox && c.IsEffectivelyVisible).Skip(skip).FirstOrDefault();
}
