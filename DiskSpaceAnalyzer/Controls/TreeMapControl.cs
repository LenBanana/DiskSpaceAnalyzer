using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    // Navigation history for breadcrumb functionality
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

        // Add breadcrumb area if we're not at the top level
        var treeMapArea = new Rect(0, 0, ActualWidth, ActualHeight);
        if (CurrentRoot != null) AddBreadcrumbArea(ref treeMapArea);

        CreateHierarchicalTreeMap(rootItems, treeMapArea, 0);
    }

    private void AddBreadcrumbArea(ref Rect treeMapArea)
    {
        const double breadcrumbHeight = 30;

        // Create breadcrumb background
        var breadcrumbBg = new Rectangle
        {
            Width = ActualWidth,
            Height = breadcrumbHeight,
            Fill = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            Stroke = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
            StrokeThickness = 1
        };

        SetLeft(breadcrumbBg, 0);
        SetTop(breadcrumbBg, 0);
        Children.Add(breadcrumbBg);

        // Add breadcrumb text
        var breadcrumbText = new TextBlock
        {
            Text = GetBreadcrumbText(),
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 5, 10, 5),
            ToolTip = "Right-click to go up one level"
        };

        SetLeft(breadcrumbText, 5);
        SetTop(breadcrumbText, 5);
        Children.Add(breadcrumbText);

        // Adjust treemap area
        treeMapArea = new Rect(0, breadcrumbHeight, ActualWidth, ActualHeight - breadcrumbHeight);
    }

    private string GetBreadcrumbText()
    {
        if (CurrentRoot == null) return "Root";

        var path = new List<string>();
        var current = CurrentRoot;

        while (current != null)
        {
            path.Insert(0, current.DisplayName);
            current = GetParentDirectory(current);
        }

        return string.Join(" > ", path);
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

            // Recursively create children if there's space and we haven't hit max depth
            if (!item.Children.Any() ||
                !(rect.Width > 50) || !(rect.Height > 50) ||
                depth >= MaxDepth) continue;
            var childrenArea = GetChildrenArea(rect, depth);
            var children = item.Children.Where(c => c.Size > 0).ToList();

            if (children.Count != 0) CreateHierarchicalTreeMap(children, childrenArea, depth + 1);
        }
    }

    private static Rect GetChildrenArea(Rect parentRect, int depth)
    {
        // Leave space for the directory label and border
        var margin = Math.Max(2, 8 - depth * 2);
        var labelHeight = Math.Max(15, 20 - depth * 2);

        return new Rect(
            parentRect.X + margin,
            parentRect.Y + labelHeight,
            Math.Max(0, parentRect.Width - 2 * margin),
            Math.Max(0, parentRect.Height - labelHeight - 2 * margin)
        );
    }

    private void CreateDirectoryRectangle(DirectoryItemViewModel item, Rect rect, int depth)
    {
        if (rect.Width < 5 || rect.Height < 5) return;

        var hasChildren = item.Children.Any() && depth < MaxDepth;
        var borderThickness = Math.Max(0.5, 2 - depth * 0.3);

        var rectangle = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = GetColorForDepthAndSize(item, depth),
            Stroke = GetBorderColor(depth),
            StrokeThickness = borderThickness,
            ToolTip = CreateToolTip(item),
            Cursor = hasChildren ? Cursors.Hand : Cursors.Arrow
        };

        // Store the mapping for click handling
        _elementToItem[rectangle] = item;

        // Add hover effects
        rectangle.MouseEnter += Rectangle_MouseEnter;
        rectangle.MouseLeave += Rectangle_MouseLeave;

        // Add click handler for navigation
        if (hasChildren || item.Children.Any()) rectangle.MouseLeftButtonDown += (_, _) => NavigateToDirectory(item);

        SetLeft(rectangle, rect.X);
        SetTop(rectangle, rect.Y);
        Children.Add(rectangle);

        // Add directory label
        AddDirectoryLabel(item, rect, depth, hasChildren);
    }

    private void Rectangle_MouseEnter(object sender, RoutedEventArgs e)
    {
        if (sender is not Rectangle rect) return;
        rect.Stroke = Brushes.Yellow;
        rect.StrokeThickness = Math.Max(rect.StrokeThickness, 2);
    }

    private void Rectangle_MouseLeave(object sender, RoutedEventArgs e)
    {
        if (sender is not Rectangle rect || !_elementToItem.TryGetValue(rect, out _)) return;
        var depth = GetDepthFromColor(rect.Fill);
        rect.Stroke = GetBorderColor(depth);
        rect.StrokeThickness = Math.Max(0.5, 2 - depth * 0.3);
    }

    private static int GetDepthFromColor(Brush fill)
    {
        if (fill is not SolidColorBrush solidBrush) return 0;
        var alpha = solidBrush.Color.A;
        return Math.Max(0, (255 - alpha) / 30);
    }

    private void AddDirectoryLabel(DirectoryItemViewModel item, Rect rect, int depth, bool hasChildren)
    {
        var fontSize = Math.Max(8, 12 - depth * 1.5);
        var labelHeight = fontSize + 4;

        if (rect.Width < 30 || rect.Height < labelHeight) return;

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Width = rect.Width - 4,
            Height = labelHeight,
            ToolTip = CreateToolTip(item),
            Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
        };

        // Add folder icon for directories with children
        if (hasChildren && rect.Width > 50)
        {
            var icon = new TextBlock
            {
                Text = "📁",
                FontSize = fontSize - 2,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0)
            };
            stackPanel.Children.Add(icon);
        }

        var textBlock = new TextBlock
        {
            Text = GetTruncatedText(item.DisplayName, rect.Width - (hasChildren ? 20 : 4), fontSize),
            Foreground = Brushes.White,
            FontSize = fontSize,
            FontWeight = depth == 0 ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 1, 2, 1)
        };

        stackPanel.Children.Add(textBlock);

        // Make the label clickable too
        if (hasChildren)
        {
            stackPanel.Cursor = Cursors.Hand;
            stackPanel.MouseLeftButtonDown += (_, _) => NavigateToDirectory(item);
            _elementToItem[stackPanel] = item;
        }

        SetLeft(stackPanel, rect.X + 2);
        SetTop(stackPanel, rect.Y + 2);
        Children.Add(stackPanel);

        // Add size label for larger rectangles
        if (!(rect.Width > 80) || !(rect.Height > 40)) return;
        var sizeLabel = new TextBlock
        {
            Text = item.FormattedSize,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            FontSize = Math.Max(7, fontSize - 2),
            Width = rect.Width - 4,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(2, 0, 4, 2)
        };

        SetLeft(sizeLabel, rect.X);
        SetTop(sizeLabel, rect.Y + rect.Height - fontSize - 2);
        Children.Add(sizeLabel);
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

    // Color scheme that considers depth
    private static SolidColorBrush GetColorForDepthAndSize(DirectoryItemViewModel item, int depth)
    {
        var baseAlpha = Math.Max(200, 255 - depth * 12);
        var alpha = (byte)baseAlpha;
        var percentage = item.PercentageOfParent;

        // Modern dark theme color palette with better contrast and visual hierarchy
        var colorVariant = (int)(item.Size % 4);

        return (colorVariant, percentage) switch
        {
            // Heat map approach - warmer colors for larger items, cooler for smaller
            // Variant 0: Red-Orange spectrum (most attention-grabbing)
            (0, >= 30) => new SolidColorBrush(Color.FromArgb(alpha, 219, 68, 68)), // Bright red
            (0, >= 20) => new SolidColorBrush(Color.FromArgb(alpha, 249, 115, 22)), // Orange
            (0, >= 10) => new SolidColorBrush(Color.FromArgb(alpha, 245, 158, 11)), // Amber
            (0, >= 5) => new SolidColorBrush(Color.FromArgb(alpha, 234, 179, 8)), // Yellow
            (0, _) => new SolidColorBrush(Color.FromArgb(alpha, 132, 204, 22)), // Lime

            // Variant 1: Blue-Purple spectrum (professional)
            (1, >= 30) => new SolidColorBrush(Color.FromArgb(alpha, 99, 102, 241)), // Indigo
            (1, >= 20) => new SolidColorBrush(Color.FromArgb(alpha, 139, 92, 246)), // Violet
            (1, >= 10) => new SolidColorBrush(Color.FromArgb(alpha, 168, 85, 247)), // Purple
            (1, >= 5) => new SolidColorBrush(Color.FromArgb(alpha, 59, 130, 246)), // Blue
            (1, _) => new SolidColorBrush(Color.FromArgb(alpha, 14, 165, 233)), // Sky blue

            // Variant 2: Teal-Green spectrum (calm, natural)
            (2, >= 30) => new SolidColorBrush(Color.FromArgb(alpha, 20, 184, 166)), // Teal
            (2, >= 20) => new SolidColorBrush(Color.FromArgb(alpha, 16, 185, 129)), // Emerald
            (2, >= 10) => new SolidColorBrush(Color.FromArgb(alpha, 34, 197, 94)), // Green
            (2, >= 5) => new SolidColorBrush(Color.FromArgb(alpha, 101, 163, 13)), // Lime green
            (2, _) => new SolidColorBrush(Color.FromArgb(alpha, 6, 182, 212)), // Cyan

            // Variant 3: Pink-Rose spectrum (distinctive)
            (3, >= 30) => new SolidColorBrush(Color.FromArgb(alpha, 236, 72, 153)), // Pink
            (3, >= 20) => new SolidColorBrush(Color.FromArgb(alpha, 251, 113, 133)), // Rose
            (3, >= 10) => new SolidColorBrush(Color.FromArgb(alpha, 244, 114, 182)), // Fuchsia
            (3, >= 5) => new SolidColorBrush(Color.FromArgb(alpha, 192, 132, 252)), // Light purple
            (3, _) => new SolidColorBrush(Color.FromArgb(alpha, 156, 163, 175)), // Cool gray

            _ => new SolidColorBrush(Color.FromArgb(alpha, 107, 114, 128)) // Neutral gray
        };
    }

    private static SolidColorBrush GetBorderColor(int depth)
    {
        var alpha = (byte)Math.Max(30, 120 - depth * 20);
        return new SolidColorBrush(Color.FromArgb(alpha, 156, 163, 175));
    }

    private static string CreateToolTip(DirectoryItemViewModel item)
    {
        var tooltip = $"📁 {item.DisplayName}\n" +
                      $"💾 Size: {item.FormattedSize}\n" +
                      $"📊 {item.PercentageOfParent:F1}% of parent\n" +
                      $"📄 Files: {item.FileCount:N0}\n" +
                      $"📁 Subdirectories: {item.DirectoryCount:N0}\n";

        if (item.HasError) tooltip += $"\n⚠️ Error: {item.Error}";

        return tooltip;
    }

    // Keep existing treemap calculation methods
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