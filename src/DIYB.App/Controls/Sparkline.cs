using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace DIYB.App.Controls;

/// <summary>Courbe compacte de l'historique de signal. Les valeurs sont normalisées
/// sur une plage fixe plutôt que sur leur propre amplitude, sinon une variation d'un
/// décibel remplirait toute la hauteur.</summary>
public sealed class Sparkline : ContentControl
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(object), typeof(Sparkline), new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Sparkline), new PropertyMetadata(-95.0, OnRenderPropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline), new PropertyMetadata(-30.0, OnRenderPropertyChanged));

    private readonly Canvas _canvas = new();
    private readonly Polyline _line = new() { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round };
    private INotifyCollectionChanged? _observed;

    public Sparkline()
    {
        _canvas.Children.Add(_line);
        Content = _canvas;
        IsTabStop = false;
        SizeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    public object? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private static void OnValuesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var sparkline = (Sparkline)sender;
        sparkline.Observe(args.NewValue as INotifyCollectionChanged);
        sparkline.Render();
    }

    private static void OnRenderPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((Sparkline)sender).Render();

    private void Observe(INotifyCollectionChanged? collection)
    {
        if (_observed is not null)
            _observed.CollectionChanged -= OnCollectionChanged;

        _observed = collection;

        if (_observed is not null)
            _observed.CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => Render();

    private void Render()
    {
        _line.Stroke = Foreground;
        _line.Points.Clear();

        if (Values is not IEnumerable<int> source)
            return;

        var values = source.ToArray();
        var width = ActualWidth;
        var height = ActualHeight;

        if (values.Length < 2 || width <= 0 || height <= 0)
            return;

        var range = Maximum - Minimum;
        if (range <= 0)
            return;

        var step = width / (values.Length - 1);
        for (var i = 0; i < values.Length; i++)
        {
            var normalized = Math.Clamp((values[i] - Minimum) / range, 0, 1);
            _line.Points.Add(new Point(i * step, height - (normalized * height)));
        }
    }
}
