using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Controls
{
    public class TreeMapControl : Canvas
    {
        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(nameof(Items), typeof(IEnumerable<DirectoryItemViewModel>),
                typeof(TreeMapControl), new PropertyMetadata(null, OnItemsChanged));

        public IEnumerable<DirectoryItemViewModel> Items
        {
            get => (IEnumerable<DirectoryItemViewModel>)GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TreeMapControl control)
            {
                control.UpdateTreeMap();
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateTreeMap();
        }

        private void UpdateTreeMap()
        {
            Children.Clear();

            if (Items == null || !Items.Any() || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var items = Items.ToList();
            var totalSize = items.Sum(i => i.Size);

            if (totalSize == 0) return;

            // Filter out items with zero size and sort by size descending
            var validItems = items.Where(i => i.Size > 0)
                .OrderByDescending(i => i.Size)
                .ToList();

            if (validItems.Count == 0) return;

            var rectangles = CalculateTreeMapRectangles(validItems, totalSize,
                new Rect(0, 0, ActualWidth, ActualHeight));

            foreach (var (item, rect) in rectangles)
            {
                CreateRectangle(item, rect);
            }
        }

        private List<(DirectoryItemViewModel Item, Rect Rect)> CalculateTreeMapRectangles(
            List<DirectoryItemViewModel> items, long totalSize, Rect area)
        {
            var result = new List<(DirectoryItemViewModel, Rect)>();

            if (items.Count == 0 || area.Width <= 0 || area.Height <= 0)
                return result;

            // Use squarified treemap algorithm
            var normalizedSizes =
                items.Select(item => (double)item.Size / totalSize * area.Width * area.Height).ToList();
            var rectangles = SquarifiedTreemap(normalizedSizes, area);

            for (var i = 0; i < items.Count && i < rectangles.Count; i++)
            {
                result.Add((items[i], rectangles[i]));
            }

            return result;
        }

        private List<Rect> SquarifiedTreemap(List<double> sizes, Rect container)
        {
            var result = new List<Rect>();
            var remaining = new Queue<double>(sizes);
            var currentArea = container;

            while (remaining.Count != 0)
            {
                var row = new List<double>();
                var bestAspectRatio = double.MaxValue;

                // Build the best row
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

                // Layout the row
                var rowRects = LayoutRow(row, currentArea);
                result.AddRange(rowRects);

                // Update remaining area
                if (remaining.Count != 0)
                {
                    currentArea = GetRemainingArea(currentArea, row.Sum());
                }
            }

            return result;
        }

        private double CalculateWorstAspectRatio(List<double> row, double width)
        {
            if (row.Count == 0 || width <= 0) return double.MaxValue;

            var sum = row.Sum();
            var min = row.Min();
            var max = row.Max();

            var height = sum / width;

            if (height <= 0) return double.MaxValue;

            var ratio1 = (width * width) / (height * min);
            var ratio2 = (height * max) / (width * width);

            return Math.Max(ratio1, ratio2);
        }

        private double GetShorterSide(Rect area)
        {
            return Math.Min(area.Width, area.Height);
        }

        private List<Rect> LayoutRow(List<double> row, Rect area)
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
                    result.Add(new Rect(currentX, area.Y,
                        Math.Max(1, width), Math.Max(1, rowHeight)));
                    currentX += width;
                }
            }
            else
            {
                var rowWidth = sum / area.Height;
                var currentY = area.Y;

                foreach (var height in row.Select(size => size / rowWidth))
                {
                    result.Add(new Rect(area.X, currentY,
                        Math.Max(1, rowWidth), Math.Max(1, height)));
                    currentY += height;
                }
            }

            return result;
        }

        private Rect GetRemainingArea(Rect currentArea, double usedArea)
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
            else
            {
                var usedWidth = usedArea / currentArea.Height;
                return currentArea with
                {
                    X = currentArea.X + usedWidth, Width = Math.Max(0, currentArea.Width - usedWidth)
                };
            }
        }

        private void CreateRectangle(DirectoryItemViewModel item, Rect rect)
        {
            // Ensure minimum size for visibility
            if (rect.Width < 1 || rect.Height < 1) return;

            var rectangle = new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Fill = GetColorForSize(item.PercentageOfParent),
                Stroke = Brushes.White,
                StrokeThickness = 0.5,
                ToolTip = CreateToolTip(item)
            };

            // Add hover effects for better UX
            rectangle.MouseEnter += (_, _) =>
            {
                rectangle.Stroke = Brushes.Yellow;
                rectangle.StrokeThickness = 2;
            };

            rectangle.MouseLeave += (_, _) =>
            {
                rectangle.Stroke = Brushes.White;
                rectangle.StrokeThickness = 0.5;
            };

            SetLeft(rectangle, rect.X);
            SetTop(rectangle, rect.Y);
            Children.Add(rectangle);

            // Add text with better sizing logic
            AddTextToRectangle(item, rect);
        }

        private void AddTextToRectangle(DirectoryItemViewModel item, Rect rect)
        {
            // More generous text display conditions
            if (!(rect.Width > 30) || !(rect.Height > 15)) return;
            var fontSize = CalculateOptimalFontSize(rect);
            var text = GetDisplayText(item.DisplayName, rect.Width, fontSize);

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = GetTextColor(item.PercentageOfParent),
                FontSize = fontSize,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Width = rect.Width - 4,
                Height = rect.Height - 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            SetLeft(textBlock, rect.X + 2);
            SetTop(textBlock, rect.Y + 2);
            Children.Add(textBlock);

            // Add size text for larger rectangles
            if (!(rect.Width > 80) || !(rect.Height > 35)) return;
            var sizeBlock = new TextBlock
            {
                Text = item.FormattedSize,
                Foreground = GetTextColor(item.PercentageOfParent),
                FontSize = Math.Max(6, fontSize - 2),
                Opacity = 0.8,
                Width = rect.Width - 4,
                Height = rect.Height - 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            SetLeft(sizeBlock, rect.X + 2);
            SetTop(sizeBlock, rect.Y + fontSize + 4);
            Children.Add(sizeBlock);
        }

        private double CalculateOptimalFontSize(Rect rect)
        {
            var baseFontSize = Math.Min(rect.Width / 8, rect.Height / 2.5);
            return Math.Max(7, Math.Min(14, baseFontSize));
        }

        private string GetDisplayText(string text, double availableWidth, double fontSize)
        {
            // Rough character width estimation
            var charWidth = fontSize * 0.6;
            var maxChars = (int)(availableWidth / charWidth) - 1;

            return text.Length > maxChars ? text.Substring(0, Math.Max(1, maxChars)) : text;
        }

        private Brush GetTextColor(double percentage)
        {
            // Use white text for dark backgrounds, dark text for light backgrounds
            return percentage >= 25 ? Brushes.White : Brushes.Black;
        }

        private string CreateToolTip(DirectoryItemViewModel item)
        {
            return $"{item.DisplayName}\n" +
                   $"Size: {item.FormattedSize}\n" +
                   $"Percentage: {item.PercentageOfParent:F1}%\n" +
                   $"Files: {item.FileCount:N0}\n" +
                   $"Folders: {item.DirectoryCount:N0}";
        }

        private Brush GetColorForSize(double percentage)
        {
            // Enhanced color scheme with better gradients
            return percentage switch
            {
                >= 40 => new SolidColorBrush(Color.FromRgb(192, 57, 43)), // Dark red
                >= 25 => new SolidColorBrush(Color.FromRgb(231, 76, 60)), // Red
                >= 15 => new SolidColorBrush(Color.FromRgb(230, 126, 34)), // Orange
                >= 8 => new SolidColorBrush(Color.FromRgb(241, 196, 15)), // Yellow
                >= 4 => new SolidColorBrush(Color.FromRgb(46, 204, 113)), // Green
                >= 2 => new SolidColorBrush(Color.FromRgb(52, 152, 219)), // Blue
                >= 1 => new SolidColorBrush(Color.FromRgb(155, 89, 182)), // Purple
                _ => new SolidColorBrush(Color.FromRgb(149, 165, 166)) // Gray
            };
        }
    }
}