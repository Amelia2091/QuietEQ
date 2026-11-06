using System.Windows;
using System.Windows.Controls;
using QuietEQ.Core;

namespace QuietEQ.App;

internal sealed class ProfileEditor : Window
{
    public DeviceProfile Profile { get; private set; }
    public ProfileEditor(DeviceProfile original, List<AudioDevice> devices, List<EqEffect> effects)
    {
        Profile = LibraryStore.Clone(original);
        Style = (Style)FindResource(typeof(Window));
        Title = "设备与 EQ 绑定"; Width = 570; Height = 555; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(30) }; Content = root;
        root.Children.Add(new TextBlock { Text = "一个设备，一个固定音色。", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 24) });
        void Label(string text) => root.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 10, 0, 7) });
        Label("配置名称");
        var name = new TextBox { Text = original.Name }; root.Children.Add(name);
        Label("Windows 输出设备");
        var options = devices.ToList();
        if (original.DeviceId.Length > 0 && options.All(d => d.Id != original.DeviceId)) options.Add(new AudioDevice(original.DeviceId, original.DeviceName + "（当前未找到）", "", 4, false, false));
        var device = new ComboBox { ItemsSource = options, DisplayMemberPath = "DisplayName", SelectedValuePath = "Id", SelectedValue = original.DeviceId }; root.Children.Add(device);
        Label("EQ 效果");
        var effect = new ComboBox { ItemsSource = effects, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = original.EffectId }; root.Children.Add(effect);
        var enabled = new CheckBox { Content = "启用这份配置", IsChecked = original.Enabled, Margin = new Thickness(0, 22, 0, 10) }; root.Children.Add(enabled);
        root.Children.Add(new TextBlock { Text = "同一设备只能启用一份配置。保存并启用后，该设备的其他配置会停用。保存后需「预览并应用」才会改变声音。", FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; root.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        var save = new Button { Content = "保存绑定", Style = (Style)FindResource("Primary") }; buttons.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (device.SelectedItem is not AudioDevice d || effect.SelectedItem is not EqEffect eq || string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(this, "请填写名称，并选择设备和 EQ 效果。"); return; }
            Profile.Name = name.Text.Trim(); Profile.DeviceId = d.Id; Profile.DeviceName = d.DisplayName; Profile.EffectId = eq.Id; Profile.Enabled = enabled.IsChecked == true;
            DialogResult = true;
        };
    }
}
