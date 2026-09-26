using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DIYB.App.Controls;

/// <summary>Disposition horizontale qui reporte à la ligne ce qui ne tient pas.
/// WinUI n'en fournit pas, et la barre d'actions doit rester entièrement visible
/// quand la fenêtre est réduite.</summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((WrapPanel)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        var limit = availableSize.Width;
        var childConstraint = new Size(limit, double.PositiveInfinity);

        double lineWidth = 0, lineHeight = 0, widest = 0, total = 0;

        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            var size = child.DesiredSize;

            if (size.Width <= 0 && size.Height <= 0)
                continue;

            var extended = lineWidth <= 0 ? size.Width : lineWidth + HorizontalSpacing + size.Width;

            if (extended > limit && lineWidth > 0)
            {
                total += lineHeight + VerticalSpacing;
                widest = Math.Max(widest, lineWidth);
                lineWidth = size.Width;
                lineHeight = size.Height;
            }
            else
            {
                lineWidth = extended;
                lineHeight = Math.Max(lineHeight, size.Height);
            }
        }

        total += lineHeight;
        widest = Math.Max(widest, lineWidth);

        return new Size(double.IsInfinity(limit) ? widest : Math.Min(widest, limit), total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var limit = finalSize.Width;
        double x = 0, y = 0, lineHeight = 0;

        foreach (var child in Children)
        {
            var size = child.DesiredSize;

            if (size.Width <= 0 && size.Height <= 0)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var extended = x <= 0 ? size.Width : x + HorizontalSpacing + size.Width;

            if (extended > limit && x > 0)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            else if (x > 0)
            {
                x += HorizontalSpacing;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(finalSize.Width, y + lineHeight);
    }
}
