using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Controls;

public class TreeMapControl : Canvas
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable<DirectoryItemViewModel>),
            typeof(TreeMapControl), new PropertyMetadata(null, OnItemsChanged));

    public static readonly DependencyProperty CurrentRootProperty =
        DependencyProperty.Register(nameof(CurrentRoot), typeof(DirectoryItemViewModel),
            typeof(TreeMapControl), new PropertyMetadata(null, OnCurrentRootChanged));

    public static readonly DependencyProperty MaxDepthProperty =
        DependencyProperty.Register(nameof(MaxDepth), typeof(int),
            typeof(TreeMapControl), new PropertyMetadata(3, OnMaxDepthChanged));

    private readonly Dictionary<UIElement, DirectoryItemViewModel> _elementToItem = new();
    private readonly Stack<DirectoryItemViewModel> _navigationHistory = new();

    static TreeMapControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TreeMapControl),
            new FrameworkPropertyMetadata(typeof(TreeMapControl)));
    }

    public IEnumerable<DirectoryItemViewModel>? Items
    {
        get => (IEnumerable<DirectoryItemViewModel>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public DirectoryItemViewModel? CurrentRoot
    {
        get => (DirectoryItemViewModel?)GetValue(CurrentRootProperty);
        set => SetValue(CurrentRootProperty, value);
    }

    public int MaxDepth
    {
        get => (int)GetValue(MaxDepthProperty);
        set => SetValue(MaxDepthProperty, value);
    }

    public event EventHandler<DirectoryItemViewModel>? DirectoryClicked;
    public event EventHandler<DirectoryItemViewModel?>? CurrentRootChanged;

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeMapControl control) control.UpdateTreeMap();
    }

    private static void OnCurrentRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeMapControl control) return;
        control.UpdateTreeMap();
        control.CurrentRootChanged?.Invoke(control, control.CurrentRoot);
    }

    private static void OnMaxDepthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeMapControl control) control.UpdateTreeMap();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateTreeMap();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        switch (e.ChangedButton)
        {
            case MouseButton.Right or MouseButton.XButton1 when CurrentRoot != null:
                NavigateUp();
                e.Handled = true;
                break;
            case MouseButton.Middle:
                NavigateToRoot();
                e.Handled = true;
                break;
        }
    }

    private void NavigateUp()
    {
        CurrentRoot = _navigationHistory.Count > 0 ? _navigationHistory.Pop() : null;
    }

    private void NavigateToRoot()
    {
        if (CurrentRoot == null) return;
        _navigationHistory.Clear();
        CurrentRoot = null;
    }

    private void UpdateTreeMap()
    {
        Children.Clear();
        _elementToItem.Clear();

        if (Items == null || !Items.Any() || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var rootItems = CurrentRoot?.Children.ToList() ?? Items.ToList();

        if (rootItems.Count == 0) return;

        var treeMapArea = new Rect(0, 0, ActualWidth, ActualHeight);
        if (CurrentRoot != null) AddBreadcrumbArea(ref treeMapArea);

        CreateHierarchicalTreeMap(rootItems, treeMapArea, 0);
    }

    private void AddBreadcrumbArea(ref Rect treeMapArea)
    {
        const double breadcrumbHeight = 45;

        //  gradient background
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            [
                new GradientStop(Color.FromRgb(58, 58, 62), 0.0),
                new GradientStop(Color.FromRgb(45, 45, 48), 1.0)
            ]
        };

        var breadcrumbBg = new Rectangle
        {
            Width = ActualWidth,
            Height = breadcrumbHeight,
            Fill = gradientBrush,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.3,
                ShadowDepth = 2,
                BlurRadius = 8
            }
        };

        SetLeft(breadcrumbBg, 0);
        SetTop(breadcrumbBg, 0);
        Children.Add(breadcrumbBg);

        //  breadcrumb container
        var breadcrumbContainer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(12, 8, 12, 8),
            Width = ActualWidth - 24,
            Height = breadcrumbHeight - 16
        };

        var breadcrumbText = new TextBlock
        {
            Text = GetBreadcrumbText(),
            Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(16, 0, 16, 0),
            ToolTip = "Right-click anywhere to go up one level"
        };

        breadcrumbContainer.Child = breadcrumbText;

        SetLeft(breadcrumbContainer, 0);
        SetTop(breadcrumbContainer, 0);
        Children.Add(breadcrumbContainer);

        treeMapArea = new Rect(0, breadcrumbHeight, ActualWidth, ActualHeight - breadcrumbHeight);
    }

    private string GetBreadcrumbText()
    {
        if (CurrentRoot == null) return "🏠 Root Directory";

        var path = new List<string>();
        var current = CurrentRoot;

        while (current != null)
        {
            path.Insert(0, current.DisplayName);
            current = GetParentDirectory(current);
        }

        return "📁 " + string.Join(" ▸ ", path);
    }

    private static DirectoryItemViewModel? GetParentDirectory(DirectoryItemViewModel item)
    {
        return item.Parent;
    }

    private void CreateHierarchicalTreeMap(List<DirectoryItemViewModel> items, Rect area, int depth)
    {
        if (items.Count == 0 || area.Width <= 1 || area.Height <= 1 || depth > MaxDepth)
            return;

        var validItems = items.Where(i => i.Size > 0)
            .OrderByDescending(i => i.Size)
            .ToList();

        if (validItems.Count == 0) return;

        var totalSize = validItems.Sum(i => i.Size);
        var rectangles = CalculateTreeMapRectangles(validItems, totalSize, area);

        foreach (var (item, rect) in rectangles)
        {
            CreateDirectoryRectangle(item, rect, depth);

            if (!item.Children.Any() ||
                !(rect.Width > 60) || !(rect.Height > 60) ||
                depth >= MaxDepth) continue;
            var childrenArea = GetChildrenArea(rect, depth);
            var children = item.Children.Where(c => c.Size > 0).ToList();

            if (children.Count != 0) CreateHierarchicalTreeMap(children, childrenArea, depth + 1);
        }
    }

    private static Rect GetChildrenArea(Rect parentRect, int depth)
    {
        var margin = Math.Max(3, 10 - depth * 2);
        var labelHeight = Math.Max(18, 25 - depth * 2);

        return new Rect(
            parentRect.X + margin,
            parentRect.Y + labelHeight,
            Math.Max(0, parentRect.Width - 2 * margin),
            Math.Max(0, parentRect.Height - labelHeight - 2 * margin)
        );
    }

    private void CreateDirectoryRectangle(DirectoryItemViewModel item, Rect rect, int depth)
    {
        if (rect.Width < 8 || rect.Height < 8) return;

        var hasChildren = item.Children.Any() && depth < MaxDepth;
        var cornerRadius = Math.Max(2, 6 - depth);

        //  rounded rectangle with gradient
        var rectangle = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = GetGradientBrush(item, depth),
            Stroke = GetBorderBrush(depth),
            StrokeThickness = 0.5,
            RadiusX = cornerRadius,
            RadiusY = cornerRadius,
            ToolTip = CreateToolTip(item),
            Cursor = hasChildren ? Cursors.Hand : Cursors.Arrow,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.15,
                ShadowDepth = 1,
                BlurRadius = 3
            }
        };

        _elementToItem[rectangle] = item;

        rectangle.MouseEnter += Rectangle_MouseEnter;
        rectangle.MouseLeave += Rectangle_MouseLeave;

        if (hasChildren || item.Children.Any()) 
            rectangle.MouseLeftButtonDown += (_, _) => NavigateToDirectory(item);

        SetLeft(rectangle, rect.X);
        SetTop(rectangle, rect.Y);
        Children.Add(rectangle);

        AddDirectoryLabel(item, rect, depth, hasChildren);
    }

    private void Rectangle_MouseEnter(object sender, RoutedEventArgs e)
    {
        if (sender is not Rectangle rect) return;

        // Smooth hover animation
        var scaleTransform = new ScaleTransform(1.0, 1.0);
        rect.RenderTransform = scaleTransform;
        rect.RenderTransformOrigin = new Point(0.5, 0.5);

        var animation = new DoubleAnimation(1.01, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);

        rect.Stroke = new SolidColorBrush(Color.FromRgb(255, 215, 0));
        rect.StrokeThickness = 2;

        // Enhance shadow on hover
        if (rect.Effect is DropShadowEffect shadow)
        {
            shadow.ShadowDepth = 3;
            shadow.BlurRadius = 6;
            shadow.Opacity = 0.3;
        }
    }

    private void Rectangle_MouseLeave(object sender, RoutedEventArgs e)
    {
        if (sender is not Rectangle rect || !_elementToItem.TryGetValue(rect, out _)) return;

        // Smooth return animation
        if (rect.RenderTransform is ScaleTransform scaleTransform)
        {
            var animation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        var depth = GetDepthFromBrush(rect.Fill);
        rect.Stroke = GetBorderBrush(depth);
        rect.StrokeThickness = 0.5;

        // Reset shadow
        if (rect.Effect is DropShadowEffect shadow)
        {
            shadow.ShadowDepth = 1;
            shadow.BlurRadius = 3;
            shadow.Opacity = 0.15;
        }
    }

    private static int GetDepthFromBrush(Brush fill)
    {
        return 0; // Simplified for now
    }

    private void AddDirectoryLabel(DirectoryItemViewModel item, Rect rect, int depth, bool hasChildren)
    {
        var fontSize = Math.Max(9, 14 - depth * 1.25);
        var labelHeight = fontSize + 6;

        if (rect.Width < 40 || rect.Height < labelHeight) return;

        //  label container with glass effect
        var labelContainer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Width = rect.Width - 8,
            Height = Math.Min(labelHeight + 4, 18),
            Effect = new BlurEffect { Radius = 1 }
        };

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 2, 8, 2)
        };

        //  folder icon
        if (hasChildren && rect.Width > 60)
        {
            var icon = new TextBlock
            {
                Text = "📂",
                FontSize = fontSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            stackPanel.Children.Add(icon);
        }

        var textBlock = new TextBlock
        {
            Text = GetTruncatedText(item.DisplayName, rect.Width - (hasChildren ? 30 : 16), fontSize),
            Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            FontSize = fontSize,
            FontWeight = depth == 0 ? FontWeights.Bold : FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 1,
                BlurRadius = 2,
                Opacity = 0.8
            }
        };

        stackPanel.Children.Add(textBlock);
        labelContainer.Child = stackPanel;

        if (hasChildren)
        {
            labelContainer.Cursor = Cursors.Hand;
            labelContainer.MouseLeftButtonDown += (_, _) => NavigateToDirectory(item);
            _elementToItem[labelContainer] = item;
        }

        SetLeft(labelContainer, rect.X + 2);
        SetTop(labelContainer, rect.Y + 2);
        Children.Add(labelContainer);

        //  size label
        if (rect is not { Width: > 100, Height: > 50 }) return;
        var sizeContainer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var sizeLabel = new TextBlock
        {
            Text = item.FormattedSize,
            Foreground = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            FontSize = Math.Max(8, fontSize - 2),
            FontWeight = FontWeights.SemiBold
        };

        sizeContainer.Child = sizeLabel;

        SetLeft(sizeContainer, rect.X + 5);
        SetTop(sizeContainer, rect.Y + rect.Height - 20);
        Children.Add(sizeContainer);
    }

    private static string GetTruncatedText(string text, double availableWidth, double fontSize)
    {
        var charWidth = fontSize * 0.6;
        var maxChars = (int)(availableWidth / charWidth);
        return text.Length > maxChars ? text.Substring(0, Math.Max(1, maxChars - 3)) + "..." : text;
    }

    private void NavigateToDirectory(DirectoryItemViewModel directory)
    {
        if (!directory.Children.Any()) return;
        if (CurrentRoot != null) _navigationHistory.Push(CurrentRoot);

        CurrentRoot = directory;
        DirectoryClicked?.Invoke(this, directory);
    }

    private static Brush GetGradientBrush(DirectoryItemViewModel item, int depth)
    {
        var percentage = item.PercentageOfParent;
        var alpha = Math.Max(200, 255 - depth * 10);
        var colorVariant = (int)(item.Size % 6);

        var colors = (colorVariant, percentage) switch
        {
            // Hot colors for large items
            (0, >= 25) => (Color.FromRgb(255, 107, 107), Color.FromRgb(255, 142, 83)),
            (0, >= 15) => (Color.FromRgb(255, 159, 67), Color.FromRgb(255, 206, 84)),
            (0, >= 8) => (Color.FromRgb(255, 218, 121), Color.FromRgb(255, 235, 153)),
            (0, _) => (Color.FromRgb(162, 155, 254), Color.FromRgb(116, 185, 255)),

            // Cool blues and purples
            (1, >= 25) => (Color.FromRgb(72, 219, 251), Color.FromRgb(116, 185, 255)),
            (1, >= 15) => (Color.FromRgb(162, 155, 254), Color.FromRgb(199, 146, 234)),
            (1, >= 8) => (Color.FromRgb(199, 146, 234), Color.FromRgb(255, 154, 158)),
            (1, _) => (Color.FromRgb(108, 92, 231), Color.FromRgb(162, 155, 254)),

            // Green nature colors
            (2, >= 25) => (Color.FromRgb(85, 239, 196), Color.FromRgb(129, 236, 236)),
            (2, >= 15) => (Color.FromRgb(129, 236, 236), Color.FromRgb(116, 185, 255)),
            (2, >= 8) => (Color.FromRgb(223, 249, 251), Color.FromRgb(85, 239, 196)),
            (2, _) => (Color.FromRgb(68, 189, 50), Color.FromRgb(85, 239, 196)),

            // Sunset colors
            (3, >= 25) => (Color.FromRgb(255, 159, 67), Color.FromRgb(255, 107, 107)),
            (3, >= 15) => (Color.FromRgb(255, 206, 84), Color.FromRgb(255, 159, 67)),
            (3, >= 8) => (Color.FromRgb(255, 235, 153), Color.FromRgb(255, 206, 84)),
            (3, _) => (Color.FromRgb(178, 190, 195), Color.FromRgb(129, 236, 236)),

            // Ocean colors
            (4, >= 25) => (Color.FromRgb(89, 98, 117), Color.FromRgb(116, 185, 255)),
            (4, >= 15) => (Color.FromRgb(116, 185, 255), Color.FromRgb(72, 219, 251)),
            (4, >= 8) => (Color.FromRgb(72, 219, 251), Color.FromRgb(85, 239, 196)),
            (4, _) => (Color.FromRgb(116, 185, 255), Color.FromRgb(162, 155, 254)),

            // Monochrome variants
            _ => (Color.FromRgb(108, 117, 125), Color.FromRgb(173, 181, 189))
        };

        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new GradientStop(Color.FromArgb((byte)alpha, colors.Item1.R, colors.Item1.G, colors.Item1.B), 0.0),
                new GradientStop(Color.FromArgb((byte)alpha, colors.Item2.R, colors.Item2.G, colors.Item2.B), 1.0)
            ]
        };
    }

    private static SolidColorBrush GetBorderBrush(int depth)
    {
        var alpha = (byte)Math.Max(30, 120 - depth * 20);
        return new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
    }

    private static FrameworkElement CreateToolTip(DirectoryItemViewModel item)
    {
        var tooltipContainer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 45, 45, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 116, 185, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                ShadowDepth = 3,
                BlurRadius = 10,
                Opacity = 0.4
            }
        };

        var tooltipContent = new StackPanel();

        // Title
        var title = new TextBlock
        {
            Text = $"📁 {item.DisplayName}",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        tooltipContent.Children.Add(title);

        // Stats
        var stats = new[]
        {
            ($"💾 Size: {item.FormattedSize}", Color.FromRgb(116, 185, 255)),
            ($"📊 {item.PercentageOfParent:F1}% of parent", Color.FromRgb(85, 239, 196)),
            ($"📄 Files: {item.FileCount:N0}", Color.FromRgb(255, 206, 84)),
            ($"📁 Subdirectories: {item.DirectoryCount:N0}", Color.FromRgb(255, 159, 67))
        };

        foreach (var (text, color) in stats)
        {
            var statBlock = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(0, 1, 0, 1)
            };
            tooltipContent.Children.Add(statBlock);
        }

        if (item.HasError)
        {
            var errorBlock = new TextBlock
            {
                Text = $"⚠️ Error: {item.Error}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                Margin = new Thickness(0, 4, 0, 0)
            };
            tooltipContent.Children.Add(errorBlock);
        }

        tooltipContainer.Child = tooltipContent;
        return tooltipContainer;
    }

    // Keep existing treemap calculation methods unchanged
    private List<(DirectoryItemViewModel Item, Rect Rect)> CalculateTreeMapRectangles(
        List<DirectoryItemViewModel> items, long totalSize, Rect area)
    {
        var result = new List<(DirectoryItemViewModel, Rect)>();

        if (items.Count == 0 || area.Width <= 0 || area.Height <= 0)
            return result;

        var normalizedSizes =
            items.Select(item => (double)item.Size / totalSize * area.Width * area.Height).ToList();
        var rectangles = SquarifiedTreemap(normalizedSizes, area);

        for (var i = 0; i < items.Count && i < rectangles.Count; i++) result.Add((items[i], rectangles[i]));

        return result;
    }

    private static List<Rect> SquarifiedTreemap(List<double> sizes, Rect container)
    {
        var result = new List<Rect>();
        var remaining = new Queue<double>(sizes);
        var currentArea = container;

        while (remaining.Count != 0)
        {
            var row = new List<double>();
            var bestAspectRatio = double.MaxValue;

            while (remaining.Count != 0)
            {
                var testRow = new List<double>(row) { remaining.Peek() };
                var aspectRatio = CalculateWorstAspectRatio(testRow, GetShorterSide(currentArea));

                if (aspectRatio <= bestAspectRatio)
                {
                    bestAspectRatio = aspectRatio;
                    row.Add(remaining.Dequeue());
                }
                else
                {
                    break;
                }
            }

            var rowRects = LayoutRow(row, currentArea);
            result.AddRange(rowRects);

            if (remaining.Count != 0) currentArea = GetRemainingArea(currentArea, row.Sum());
        }

        return result;
    }

    private static double CalculateWorstAspectRatio(List<double> row, double width)
    {
        if (row.Count == 0 || width <= 0) return double.MaxValue;

        var sum = row.Sum();
        var min = row.Min();
        var max = row.Max();
        var height = sum / width;

        if (height <= 0) return double.MaxValue;

        var ratio1 = width * width / (height * min);
        var ratio2 = height * max / (width * width);

        return Math.Max(ratio1, ratio2);
    }

    private static double GetShorterSide(Rect area)
    {
        return Math.Min(area.Width, area.Height);
    }

    private static List<Rect> LayoutRow(List<double> row, Rect area)
    {
        var result = new List<Rect>();
        var sum = row.Sum();

        if (sum <= 0) return result;

        var isHorizontal = area.Width >= area.Height;

        if (isHorizontal)
        {
            var rowHeight = sum / area.Width;
            var currentX = area.X;

            foreach (var width in row.Select(size => size / rowHeight))
            {
                result.Add(new Rect(currentX, area.Y, Math.Max(1, width), Math.Max(1, rowHeight)));
                currentX += width;
            }
        }
        else
        {
            var rowWidth = sum / area.Height;
            var currentY = area.Y;

            foreach (var height in row.Select(size => size / rowWidth))
            {
                result.Add(new Rect(area.X, currentY, Math.Max(1, rowWidth), Math.Max(1, height)));
                currentY += height;
            }
        }

        return result;
    }

    private static Rect GetRemainingArea(Rect currentArea, double usedArea)
    {
        var isHorizontal = currentArea.Width >= currentArea.Height;

        if (isHorizontal)
        {
            var usedHeight = usedArea / currentArea.Width;
            return currentArea with
            {
                Y = currentArea.Y + usedHeight, Height = Math.Max(0, currentArea.Height - usedHeight)
            };
        }

        var usedWidth = usedArea / currentArea.Height;
        return currentArea with { X = currentArea.X + usedWidth, Width = Math.Max(0, currentArea.Width - usedWidth) };
    }
}
