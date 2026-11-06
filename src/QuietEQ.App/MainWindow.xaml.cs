using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using QuietEQ.Core;

namespace QuietEQ.App;

public partial class MainWindow : Window
{
    private readonly LibraryStore store;
    private LibraryState state;
    private List<AudioDevice> devices = [];
    private IReadOnlyList<AutoEqEntry> index = [];
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly AutoEqClient autoEq;
    private readonly bool isolated;
    private readonly bool smoke;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool indexBusy;
    private bool indexRequested;
    private string page = "devices";

    public MainWindow(string dataDirectory, bool isolated, bool smoke)
    {
        this.isolated = isolated; this.smoke = smoke;
        store = new LibraryStore(dataDirectory);
        state = store.Load();
        autoEq = new AutoEqClient(http, Path.Combine(dataDirectory, "cache"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("QuietEQ/0.1");
        InitializeComponent();
        if (isolated)
        {
            Title += " — 隔离演示"; ModeLabel.Text = "演示数据  /  不影响系统音频";
            state.ConfigDirectory = Path.Combine(dataDirectory, "sandbox-config");
            Directory.CreateDirectory(state.ConfigDirectory);
            if (!File.Exists(Path.Combine(state.ConfigDirectory, "config.txt"))) File.WriteAllText(Path.Combine(state.ConfigDirectory, "config.txt"), "# isolated original\n");
            devices = [new("{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}", "耳机", "TANCHJIM-SPACE PRO AT", 1, true, true), new("{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}", "耳机", "Pi8-Case", 1, false, true), new("{0.0.0.00000000}.{33333333-3333-3333-3333-333333333333}", "Mi Monitor", "NVIDIA High Definition Audio", 1, false, false)];
            if (state.Effects.Count == 0)
            {
                var a = EqCodec.Parse("Preamp: -5.2 dB\nFilter 1: ON LSC Fc 105 Hz Gain 4.5 dB Q 0.707\nFilter 2: ON PK Fc 2800 Hz Gain -3.2 dB Q 1.2\nFilter 3: ON HSC Fc 9000 Hz Gain -1.5 dB Q 0.707", "TANCHJIM Origin · 演示音色");
                var b = EqCodec.Parse("Preamp: -3.8 dB\nFilter 1: ON PK Fc 120 Hz Gain -2 dB Q 0.8\nFilter 2: ON PK Fc 4200 Hz Gain 3 dB Q 1.7", "Bowers & Wilkins Pi8 · 演示音色");
                state.Effects.AddRange([a, b]);
                state.Profiles.AddRange([new() { Name = "桌面聆听", DeviceId = devices[0].Id, DeviceName = devices[0].DisplayName, EffectId = a.Id }, new() { Name = "无线耳机", DeviceId = devices[1].Id, DeviceName = devices[1].DisplayName, EffectId = b.Id }]);
                store.Save(state);
            }
        }
        else state.ConfigDirectory ??= DeviceService.ConfigDirectory();
        Loaded += async (_, _) =>
        {
            RefreshDevices(); Render(); ShowPage("devices");
            if (smoke) await SmokeAsync();
            else timer.Start();
        };
        timer.Tick += (_, _) => { RefreshDevices(); UpdateApplicationStatus(); };
        Closed += (_, _) => { timer.Stop(); http.Dispose(); };
    }

    private void Navigate(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);
    private void ShowPage(string target)
    {
        page = target;
        DevicesPage.Visibility = target == "devices" ? Visibility.Visible : Visibility.Collapsed;
        EffectsPage.Visibility = target == "effects" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = target == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        AutoPage.Visibility = target == "autoeq" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in new[] { DevicesNav, EffectsNav, ProfilesNav, AutoNav })
        {
            b.Background = (string)b.Tag == target ? new SolidColorBrush(Color.FromRgb(47, 78, 63)) : Brushes.Transparent;
            b.Foreground = (string)b.Tag == target ? Brushes.White : new SolidColorBrush(Color.FromRgb(191, 207, 199));
        }
        (PageTitle.Text, PageSubtitle.Text) = target switch
        {
            "effects" => ("你的音色，独立收藏。", "效果只描述声音。一个 EQ 可以被多份设备配置引用。"),
            "profiles" => ("固定搭配，自然切换。", "为每台 Windows 输出设备保留一份启用配置。"),
            "autoeq" => ("从一副耳机，开始。", "AutoEQ 目录已融入工作空间，无需跳转网页下载。"),
            _ => ("每台设备，都有自己的声音。", "一次绑定，长期生效。切换输出，EQ 随之切换。")
        };
        if (target == "autoeq" && !indexRequested && !smoke) { indexRequested = true; _ = LoadIndexAsync(); }
    }

    private void RefreshDevices()
    {
        try
        {
            if (!isolated)
            {
                var next = DeviceService.Enumerate();
                if (devices.SequenceEqual(next)) return;
                devices = next;
            }
            RenderDevices();
        }
        catch (Exception ex) { StatusText.Text = "读取输出设备失败：" + ex.Message; }
    }
    private void RefreshClick(object sender, RoutedEventArgs e) { RefreshDevices(); UpdateApplicationStatus(); }
    private void FilterDevices(object sender, RoutedEventArgs e) { if (IsLoaded) RenderDevices(); }
    private void RenderDevices()
    {
        var defaultDevice = devices.FirstOrDefault(d => d.IsDefault);
        DefaultDeviceName.Text = defaultDevice?.DisplayName ?? "没有默认输出设备";
        var binding = state.Profiles.FirstOrDefault(p => p.Enabled && p.DeviceId == defaultDevice?.Id);
        DefaultBinding.Text = binding is null ? "尚未绑定 EQ · 从下方选择设备开始" : "已保存绑定：" + EffectName(binding.EffectId);
        DeviceCounts.Text = $"{devices.Count(d => d.State == 1)} 台在线  /  {state.Profiles.Count(p => p.Enabled)} 份启用配置";
        DevicesList.ItemsSource = devices.Where(d => ShowOffline.IsChecked == true || d.State == 1).Select(d =>
        {
            var p = state.Profiles.FirstOrDefault(p => p.DeviceId == d.Id && p.Enabled);
            return new DeviceRow(d, d.DisplayName, d.Status + (d.IsDefault ? " · Windows 默认输出" : ""), p is null ? "未绑定 EQ" : EffectName(p.EffectId), d.ApoStatus);
        }).ToList();
    }
    private string EffectName(string id) => state.Effects.FirstOrDefault(e => e.Id == id)?.Name ?? "EQ 不存在";
    private void Render()
    {
        var effectId = (EffectsGrid.SelectedItem as EqEffect)?.Id;
        var profileId = (ProfilesGrid.SelectedItem as ProfileRow)?.Profile.Id;
        EffectsGrid.ItemsSource = state.Effects.ToList();
        EffectsGrid.SelectedItem = state.Effects.FirstOrDefault(e => e.Id == effectId);
        EffectsEmpty.Visibility = state.Effects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EffectsCount.Text = $"{state.Effects.Count} 个 EQ 效果 · 本地保存";
        var profiles = state.Profiles.Select(p => new ProfileRow(p, p.Enabled ? "启用" : "停用", p.Name, p.DeviceName, EffectName(p.EffectId))).ToList();
        ProfilesGrid.ItemsSource = profiles;
        ProfilesGrid.SelectedItem = profiles.FirstOrDefault(p => p.Profile.Id == profileId);
        ProfilesEmpty.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderDevices(); UpdateApplicationStatus();
    }
    private void UpdateApplicationStatus()
    {
        try
        {
            if (state.ConfigDirectory is null) { StatusText.Text = "未找到 Equalizer APO。请指定配置目录，并为输出设备安装 APO。"; return; }
            if (state.LastAppliedHash is null) { StatusText.Text = "尚未应用 · 保存效果和绑定后，点击「预览并应用」。"; return; }
            if (ConfigInstaller.ReadHash(state.ConfigDirectory) != state.LastAppliedHash) { StatusText.Text = "配置被外部修改 · 请检查 Peace 或其他配置工具。"; return; }
            if (state.LastAppliedRevision != ConfigCompiler.Revision(state)) { StatusText.Text = "存在未应用的更改 · 当前音频仍使用上次应用的配置。"; return; }
            StatusText.Text = "配置已写入 · 关闭本窗口后继续保留；实际声音取决于各设备的 APO 安装。";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Commit(Action<LibraryState> mutation)
    {
        var candidate = LibraryStore.Clone(state);
        mutation(candidate);
        store.Save(candidate);
        state = candidate;
        Render();
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, Friendly(ex), "未能完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private static string Friendly(Exception ex) => ex is UnauthorizedAccessException ? "当前用户没有写入权限。请以管理员身份运行 QuietEQ 后再应用。\n\n" + ex.Message : ex.Message;
    private EqEffect RequireEffect() => EffectsGrid.SelectedItem as EqEffect ?? throw new InvalidOperationException("请先选择一个 EQ 效果。");
    private DeviceProfile RequireProfile() => (ProfilesGrid.SelectedItem as ProfileRow)?.Profile ?? throw new InvalidOperationException("请先选择一份配置。");

    private void NewEffect(object sender, RoutedEventArgs e) => Guard(() => EditAndSave(new EqEffect { Bands = [new()] }, true));
    private void EditEffect(object sender, RoutedEventArgs e) => Guard(() => EditAndSave(RequireEffect(), false));
    private void EditAndSave(EqEffect effect, bool isNew)
    {
        var editor = new EffectEditor(LibraryStore.Clone(effect)) { Owner = this };
        if (editor.ShowDialog() != true) return;
        Commit(s => { if (!isNew) s.Effects.RemoveAll(x => x.Id == effect.Id); s.Effects.Add(editor.Effect); });
    }
    private void ImportEffect(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new OpenFileDialog { Filter = "EQ 文本文件 (*.txt)|*.txt", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        var imported = dialog.FileNames.Select(file =>
        {
            if (new FileInfo(file).Length > 1_000_000) throw new InvalidDataException("文件过大：" + Path.GetFileName(file));
            return EqCodec.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
        }).ToList();
        Commit(s => s.Effects.AddRange(imported));
    });
    private void ExportEffect(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var effect = RequireEffect();
        var safeName = string.Concat(effect.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog { Filter = "EQ 文本文件 (*.txt)|*.txt", FileName = safeName + ".txt" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, EqCodec.Serialize(effect), new System.Text.UTF8Encoding(false));
    });
    private void DuplicateEffect(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var copy = LibraryStore.Clone(RequireEffect()); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " · 副本";
        Commit(s => s.Effects.Add(copy));
    });
    private void DeleteEffect(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var effect = RequireEffect();
        if (state.Profiles.Any(p => p.EffectId == effect.Id)) throw new InvalidOperationException("此 EQ 正被配置引用，请先修改或删除对应配置。");
        if (MessageBox.Show(this, $"删除效果「{effect.Name}」？", "删除效果", MessageBoxButton.OKCancel) == MessageBoxResult.OK) Commit(s => s.Effects.RemoveAll(x => x.Id == effect.Id));
    });

    private void BindDevice(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var d = (AudioDevice)((Button)sender).Tag;
        var current = state.Profiles.FirstOrDefault(p => p.DeviceId == d.Id && p.Enabled);
        EditAndSaveProfile(current ?? new DeviceProfile { DeviceId = d.Id, DeviceName = d.DisplayName, Name = d.DisplayName }, current is null);
    });
    private void NewProfile(object sender, RoutedEventArgs e) => Guard(() => EditAndSaveProfile(new DeviceProfile(), true));
    private void EditProfile(object sender, RoutedEventArgs e) => Guard(() => EditAndSaveProfile(RequireProfile(), false));
    private void EditAndSaveProfile(DeviceProfile profile, bool isNew)
    {
        if (state.Effects.Count == 0) { ShowPage("effects"); throw new InvalidOperationException("请先导入、创建或从 AutoEQ 下载一个效果。"); }
        var dialog = new ProfileEditor(profile, devices, state.Effects) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var result = dialog.Profile;
        Commit(s =>
        {
            if (!isNew) s.Profiles.RemoveAll(p => p.Id == profile.Id);
            if (result.Enabled) foreach (var p in s.Profiles.Where(p => p.DeviceId == result.DeviceId)) p.Enabled = false;
            s.Profiles.Add(result);
        });
    }
    private void ToggleProfile(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var profile = RequireProfile();
        Commit(s =>
        {
            var p = s.Profiles.Single(x => x.Id == profile.Id);
            if (!p.Enabled) foreach (var other in s.Profiles.Where(x => x.DeviceId == p.DeviceId)) other.Enabled = false;
            p.Enabled = !profile.Enabled;
        });
    });
    private void DeleteProfile(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var p = RequireProfile();
        if (MessageBox.Show(this, $"删除配置「{p.Name}」？设备会在下次应用后解除此绑定。", "删除配置", MessageBoxButton.OKCancel) == MessageBoxResult.OK) Commit(s => s.Profiles.RemoveAll(x => x.Id == p.Id));
    });

    private void ApplyConfig(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var directory = state.ConfigDirectory ?? throw new InvalidOperationException("请先选择 Equalizer APO 配置目录。");
        var hash = ConfigInstaller.ReadHash(directory);
        var preview = ConfigCompiler.Compile(state, @"QuietEQ\preview");
        var missing = state.Profiles.Where(p => p.Enabled && !devices.Any(d => d.Id == p.DeviceId && d.ApoRegistered)).Select(p => p.DeviceName).ToList();
        var note = $"将写入 {state.Profiles.Count(p => p.Enabled)} 份设备绑定，替换 {Path.Combine(directory, "config.txt")}。\n现有配置会先备份。未绑定设备不添加 EQ。";
        if (missing.Count > 0) note += "\n以下设备未检测到 APO 登记，应用后仍需在 Configurator 中安装：" + string.Join("、", missing);
        var dialog = Dialogs.Preview(this, note, preview.Text, "备份并应用");
        if (dialog.ShowDialog() != true) return;
        if (!isolated && Process.GetProcessesByName("Peace").Length > 0) throw new InvalidOperationException("Peace 正在运行，可能覆盖配置。请退出 Peace 后重新应用。");
        var receipt = new ConfigInstaller().Apply(state, directory, hash);
        Commit(s => { s.LastAppliedHash = receipt.ConfigHash; s.LastAppliedRevision = receipt.Revision; });
    });
    private void RestoreConfig(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (MessageBox.Show(this, "恢复上一次应用前的 config.txt？效果库和已保存绑定将保留。", "恢复配置", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        new ConfigInstaller().Restore(state.ConfigDirectory ?? throw new InvalidOperationException("未设置配置目录。"));
        Commit(s => { s.LastAppliedHash = null; s.LastAppliedRevision = null; });
    });
    private void ChooseConfigDirectory(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (isolated) { MessageBox.Show(this, "隔离模式固定使用演示目录：\n" + state.ConfigDirectory); return; }
        var dialog = new OpenFolderDialog { Title = "选择 Equalizer APO 的 config 文件夹" };
        if (dialog.ShowDialog(this) == true) Commit(s => { s.ConfigDirectory = dialog.FolderName; s.LastAppliedHash = null; s.LastAppliedRevision = null; });
    });
    private void OpenConfigurator(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (isolated) throw new InvalidOperationException("隔离演示不启动系统配置工具。");
        var exe = Path.Combine(DeviceService.InstallDirectory() ?? "", "Configurator.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("未找到 Equalizer APO Configurator，请先安装 Equalizer APO。");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    });

    private async void ReloadAutoEq(object sender, RoutedEventArgs e) => await LoadIndexAsync();
    private async Task LoadIndexAsync()
    {
        if (indexBusy) return;
        indexBusy = true; RefreshIndex.IsEnabled = false; AutoStatus.Text = "正在连接 AutoEQ 目录…";
        try { index = await autoEq.LoadIndexAsync(); FilterIndex(); }
        catch (Exception ex) { AutoStatus.Text = "目录加载失败，可点击更新重试：" + ex.Message; }
        finally { indexBusy = false; RefreshIndex.IsEnabled = true; }
    }
    private void SearchAutoEq(object sender, TextChangedEventArgs e) { if (AutoGrid is not null) FilterIndex(); }
    private void FilterIndex()
    {
        var matches = AutoEqClient.Search(index, AutoSearch.Text);
        AutoGrid.ItemsSource = matches.Take(250).ToList();
        AutoStatus.Text = $"{index.Count:N0} 条目录 · 找到 {matches.Count:N0} 条" + (matches.Count > 250 ? "（显示前 250 条，请细化搜索）" : "") + (autoEq.UsedCache ? $" · 离线缓存 {autoEq.CacheTime:g}" : "");
        if (matches.Count == 0 && index.Count > 0) AutoStatus.Text += " · 可尝试品牌英文名或缩短型号";
    }
    private async void DownloadEffect(object sender, RoutedEventArgs e)
    {
        if (AutoGrid.SelectedItem is not AutoEqEntry selected) { MessageBox.Show(this, "请先选择一条 AutoEQ 结果。"); return; }
        DownloadButton.IsEnabled = false; AutoStatus.Text = "正在下载 " + selected.Name + "…";
        try
        {
            var effect = await autoEq.DownloadAsync(selected, DownloadFormat.SelectedIndex == 1);
            Commit(s => s.Effects.Add(effect));
            AutoStatus.Text = $"已下载「{effect.Name}」到效果库。现在可前往「配置」绑定设备。";
        }
        catch (Exception ex) { AutoStatus.Text = "下载失败，未修改效果库：" + ex.Message; }
        finally { DownloadButton.IsEnabled = true; }
    }

    private async Task SmokeAsync()
    {
        try
        {
            var output = Path.Combine(store.Root, "screenshots"); Directory.CreateDirectory(output);
            index = AutoEqClient.ParseIndex("- [Sennheiser HD 600](./oratory1990/over-ear/Sennheiser%20HD%20600) by oratory1990\n- [TANCHJIM Origin](./example/in-ear/TANCHJIM%20Origin) by Demo measurement\n");
            FilterIndex();
            foreach (var target in new[] { "devices", "effects", "profiles", "autoeq" })
            {
                ShowPage(target); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture(this, Path.Combine(output, target + ".png"));
            }
            var editor = new EffectEditor(LibraryStore.Clone(state.Effects[0])) { Owner = this };
            editor.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(editor, Path.Combine(output, "editor.png")); editor.Close();
            var profile = new ProfileEditor(state.Profiles[0], devices, state.Effects) { Owner = this };
            profile.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(profile, Path.Combine(output, "binding.png")); profile.Close();
            var sample = ConfigCompiler.Compile(state, @"QuietEQ\preview");
            var preview = Dialogs.Preview(this, "演示：两台设备独立绑定 EQ。", sample.Text, "备份并应用");
            preview.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(preview, Path.Combine(output, "preview.png")); preview.Close();
            File.WriteAllText(Path.Combine(store.Root, "ui-smoke.txt"), "PASS: four navigation pages, effect editor, binding editor, config preview rendered. Isolated data only.");
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(store.Root, "ui-smoke.txt"), ex.ToString()); Application.Current.Shutdown(1); }
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed record DeviceRow(AudioDevice Device, string Name, string StateText, string BindingText, string ApoText);
    private sealed record ProfileRow(DeviceProfile Profile, string Status, string Name, string DeviceName, string EffectName);
}
