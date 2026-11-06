using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using QuietEQ.Core;

namespace QuietEQ.App;

public partial class EffectEditor : Window
{
    public new EqEffect Effect { get; }
    private readonly ObservableCollection<EqBand> bands;
    private readonly ObservableCollection<GraphicPoint> points;
    private readonly bool graphic;
    public EffectEditor(EqEffect effect)
    {
        Effect = effect;
        bands = new(effect.Bands); points = new(effect.Graphic); graphic = effect.Graphic.Count > 0;
        InitializeComponent();
        EffectName.Text = effect.Name; PreampBox.Text = EqCodec.N(effect.Preamp);
        FormatLabel.Text = graphic ? "GRAPHIC EQ · 图示均衡" : "PARAMETRIC EQ · 参数均衡";
        if (!graphic)
        {
            BandsGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = BindingFor("Enabled"), Width = 65 });
            BandsGrid.Columns.Add(new DataGridComboBoxColumn { Header = "类型", SelectedItemBinding = BindingFor("Type"), ItemsSource = new[] { "PK", "LSC", "HSC" }, Width = 110 });
        }
        BandsGrid.Columns.Add(new DataGridTextColumn { Header = "频率 / Hz", Binding = BindingFor("Frequency"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        BandsGrid.Columns.Add(new DataGridTextColumn { Header = "增益 / dB", Binding = BindingFor("Gain"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        if (!graphic) BandsGrid.Columns.Add(new DataGridTextColumn { Header = "Q", Binding = BindingFor("Q"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        BandsGrid.ItemsSource = graphic ? points : bands;
        Loaded += (_, _) => Draw();
    }
    private static Binding BindingFor(string path) => new(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true, NotifyOnValidationError = true, ConverterCulture = CultureInfo.InvariantCulture };
    private void Sync() { Effect.Bands = bands.ToList(); Effect.Graphic = points.OrderBy(p => p.Frequency).ToList(); }
    private void AddBand(object sender, RoutedEventArgs e)
    {
        if (graphic) points.Add(new GraphicPoint { Frequency = Math.Min(24000, (points.LastOrDefault()?.Frequency ?? 10) + 10), Gain = 0 });
        else bands.Add(new EqBand());
        Sync(); Draw();
    }
    private void RemoveBand(object sender, RoutedEventArgs e)
    {
        if (BandsGrid.SelectedItem is EqBand band) bands.Remove(band);
        if (BandsGrid.SelectedItem is GraphicPoint point) points.Remove(point);
        Sync(); Draw();
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!BandsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !BandsGrid.CommitEdit(DataGridEditingUnit.Row, true) || HasError(BandsGrid)) throw new ArgumentException("请修正红框内的数字。");
            if (!double.TryParse(PreampBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var preamp)) throw new ArgumentException("前级增益请输入数字，使用小数点。");
            Sync(); Effect.Preamp = preamp; Effect.Name = EffectName.Text.Trim();
            if (graphic && points.Count == 0) throw new ArgumentException("图示 EQ 至少保留一个频段。");
            EqCodec.Validate(Effect); DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "检查 EQ 参数", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private static bool HasError(DependencyObject node)
    {
        if (Validation.GetHasError(node)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (HasError(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
    private void BandEdited(object sender, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(() => { Sync(); Draw(); }, DispatcherPriority.Background);
    private void PreampChanged(object sender, TextChangedEventArgs e)
    {
        if (double.TryParse(PreampBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)) Effect.Preamp = value;
        if (IsLoaded) Draw();
    }
    private void PlotChanged(object sender, SizeChangedEventArgs e) { if (IsLoaded) Draw(); }
    private void Draw()
    {
        if (Plot is null || Plot.ActualWidth < 50) return;
        Plot.Children.Clear();
        var width = Plot.ActualWidth - 58; var height = Plot.ActualHeight - 33;
        double X(double f) => 36 + Math.Log10(f / 20) / 3 * width;
        var samples = Enumerable.Range(0, 301).Select(i => (F: 20 * Math.Pow(1000, i / 300.0), V: 0.0)).Select(p => (p.F, V: FrequencyResponse.At(Effect, p.F))).ToList();
        var limit = Math.Max(12, Math.Ceiling(samples.Where(p => double.IsFinite(p.V)).Select(p => Math.Abs(p.V)).DefaultIfEmpty(12).Max() / 6) * 6);
        limit = Math.Min(120, limit);
        double Y(double db) => 6 + (limit - Math.Clamp(db, -limit, limit)) / (2 * limit) * height;
        foreach (var db in new[] { limit, 0, -limit })
        {
            Plot.Children.Add(new Line { X1 = 36, X2 = width + 36, Y1 = Y(db), Y2 = Y(db), Stroke = new SolidColorBrush(Color.FromRgb(57, 83, 69)), StrokeThickness = 1 });
            Label((db > 0 ? "+" : "") + EqCodec.N(db), 0, Y(db) - 7);
        }
        foreach (var f in new[] { 20, 100, 1000, 10000, 20000 })
        {
            Plot.Children.Add(new Line { X1 = X(f), X2 = X(f), Y1 = 6, Y2 = height + 6, Stroke = new SolidColorBrush(Color.FromRgb(42, 67, 55)), StrokeThickness = 1 });
            Label(f >= 1000 ? f / 1000 + "k" : f.ToString(), X(f) - 9, height + 12);
        }
        var curve = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(180, 222, 143)), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        foreach (var p in samples.Where(p => double.IsFinite(p.V))) curve.Points.Add(new Point(X(p.F), Y(p.V)));
        Plot.Children.Add(curve);
    }
    private void Label(string text, double x, double y)
    {
        var label = new TextBlock { Text = text, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(151, 176, 155)) };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y); Plot.Children.Add(label);
    }
}
