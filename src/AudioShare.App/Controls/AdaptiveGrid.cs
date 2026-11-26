using System.Windows;
using System.Windows.Controls;

namespace AudioShare.App.Controls;

/// <summary>
/// 按可用宽度自动决定列数的网格：每列至少 MinItemWidth 宽，最多 MaxColumns 列，
/// 每行的高度取这一行里最高的那一项。窗口变宽就多放几列，变窄就少放几列，控件本身不缩放。
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(300.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>当前的列数（布局后更新）。</summary>
    public int Columns { get; private set; } = 1;

    private int ColumnsFor(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return 1;
        int columns = (int)Math.Floor(width / Math.Max(1, MinItemWidth));
        return Math.Clamp(columns, 1, Math.Max(1, MaxColumns));
    }

    protected override Size MeasureOverride(Size available)
    {
        var children = InternalChildren;
        int columns = ColumnsFor(available.Width);
        double itemWidth = double.IsInfinity(available.Width) ? MinItemWidth : available.Width / columns;
        double total = 0, rowHeight = 0;
        for (int i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            if (i % columns == columns - 1 || i == children.Count - 1)
            {
                total += rowHeight;
                rowHeight = 0;
            }
        }
        double width = double.IsInfinity(available.Width) ? itemWidth * Math.Min(columns, Math.Max(1, children.Count)) : available.Width;
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var children = InternalChildren;
        int columns = ColumnsFor(final.Width);
        Columns = columns;
        double itemWidth = final.Width / columns;
        double y = 0;
        for (int start = 0; start < children.Count; start += columns)
        {
            int end = Math.Min(start + columns, children.Count);
            double rowHeight = 0;
            for (int i = start; i < end; i++) rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            for (int i = start; i < end; i++)
                children[i].Arrange(new Rect((i - start) * itemWidth, y, itemWidth, rowHeight));
            y += rowHeight;
        }
        return final;
    }
}
