// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Frosted-glass clipboard history dialog displaying the last 10 copied texts.
/// Features Apple-inspired spring animations, hover sheens, keyboard shortcuts (1-9, Esc),
/// and one-click copy with instant visual checkmark feedback.
/// </summary>
public partial class ClipboardDialog : Window
{
    private readonly ClipboardService _clipboardService;
    private readonly bool _anim = SystemParameters.ClientAreaAnimation;
    private bool _closing;

    public static void ShowHistory(ClipboardService clipboardService, Window? owner = null)
    {
        var dlg = new ClipboardDialog(clipboardService)
        {
            Owner = owner
        };
        dlg.ShowDialog();
    }

    public ClipboardDialog(ClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
        InitializeComponent();

        CloseBtn.Click += (_, _) => Finish(false);
        ClearBtn.Click += (_, _) => OnClear();

        Card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(false);
                e.Handled = true;
                return;
            }

            int index = -1;
            if (e.Key >= Key.D1 && e.Key <= Key.D9) index = e.Key - Key.D1;
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) index = e.Key - Key.NumPad1;
            else if (e.Key is Key.D0 or Key.NumPad0) index = 9;

            if (index >= 0 && index < _clipboardService.Count)
            {
                QuickPick(index);
                e.Handled = true;
            }
        };

        PopulateList();
        Loaded += (_, _) => Open();
    }

    private void PopulateList()
    {
        ItemsStack.Children.Clear();
        var history = _clipboardService.History;

        if (history.Count == 0)
        {
            ListScroll.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ClearBtn.Visibility = Visibility.Collapsed;
            SubtitleText.Text = "0 items stored";
            return;
        }

        ListScroll.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        ClearBtn.Visibility = Visibility.Visible;
        SubtitleText.Text = $"{history.Count} of {ClipboardService.MaxHistoryCount} items • Click to copy";

        for (int i = 0; i < history.Count; i++)
        {
            int index = i;
            string text = history[i];
            var card = CreateItemCard(index, text);
            ItemsStack.Children.Add(card);
        }
    }

    private Border CreateItemCard(int index, string fullText)
    {
        var normalBg = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0xFF, 0x24, 0x29, 0x38), 0),
                new(Color.FromArgb(0xFF, 0x18, 0x1B, 0x25), 0.38),
                new(Color.FromArgb(0xFF, 0x10, 0x12, 0x19), 1)
            },
            new Point(0.1, 0),
            new Point(0.9, 1));

        var normalBorder = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0),
                new(Color.FromArgb(0x30, 0x3B, 0x74, 0xB6), 0.35),
                new(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 1)
            },
            new Point(0, 0),
            new Point(0.85, 1));

        var hoverBg = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0xFF, 0x34, 0x3D, 0x54), 0),
                new(Color.FromArgb(0xFF, 0x23, 0x29, 0x39), 0.38),
                new(Color.FromArgb(0xFF, 0x16, 0x19, 0x23), 1)
            },
            new Point(0.1, 0),
            new Point(0.9, 1));

        var hoverBorder = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 0),
                new(Color.FromArgb(0x70, 0x60, 0xA5, 0xFA), 0.35),
                new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1)
            },
            new Point(0, 0),
            new Point(0.85, 1));

        var itemBorder = new Border
        {
            Background = normalBg,
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(11, 8, 11, 8),
            Margin = new Thickness(0, 0, 0, 7),
            Cursor = Cursors.Hand,
            Focusable = false,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        AutomationProperties.SetName(itemBorder, $"Clipboard item {index + 1}: {fullText}");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Numeric Chip (1..10) with frosted jewel gradient
        var chip = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0xFF, 0x2C, 0x33, 0x47), 0),
                    new(Color.FromArgb(0xFF, 0x1A, 0x1E, 0x2B), 1)
                },
                new Point(0, 0),
                new Point(0, 1)),
            BorderBrush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), 0),
                    new(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 1)
                },
                new Point(0, 0),
                new Point(0, 1)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        var chipText = new TextBlock
        {
            Text = (index + 1 == 10 ? "0" : (index + 1).ToString()),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8)), // Luminous cyan
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        chip.Child = chipText;
        Grid.SetColumn(chip, 0);
        grid.Children.Add(chip);

        // Text & length preview
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var preview = new TextBlock
        {
            Text = IslandLogic.FormatClipboardPreview(fullText, 90),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 36
        };
        var meta = new TextBlock
        {
            Text = $"{fullText.Length:N0} characters" + (fullText.Contains('\n') ? " • multi-line" : ""),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        textStack.Children.Add(preview);
        textStack.Children.Add(meta);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        // Copy button / feedback icon
        var actionIconBorder = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), 0),
                    new(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1)
                },
                new Point(0, 0),
                new Point(0, 1)),
            BorderBrush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0),
                    new(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF), 1)
                },
                new Point(0, 0),
                new Point(0, 1)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        var glyph = new TextBlock
        {
            Text = "\uE8C8", // Copy glyph
            FontFamily = (FontFamily)FindResource("Icons"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionIconBorder.Child = glyph;
        Grid.SetColumn(actionIconBorder, 2);
        grid.Children.Add(actionIconBorder);

        itemBorder.Child = grid;

        // Hover micro-animations
        itemBorder.MouseEnter += (_, _) =>
        {
            itemBorder.Background = hoverBg;
            itemBorder.BorderBrush = hoverBorder;
            actionIconBorder.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF), 0),
                    new(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 1)
                },
                new Point(0, 0),
                new Point(0, 1));
            glyph.Foreground = Brushes.White;
        };

        itemBorder.MouseLeave += (_, _) =>
        {
            itemBorder.Background = normalBg;
            itemBorder.BorderBrush = normalBorder;
            actionIconBorder.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), 0),
                    new(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1)
                },
                new Point(0, 0),
                new Point(0, 1));
            glyph.Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
        };

        // Click to copy
        itemBorder.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            await TriggerCopyFeedbackAsync(itemBorder, chip, glyph, fullText);
        };

        return itemBorder;
    }

    private async Task TriggerCopyFeedbackAsync(Border card, Border chip, TextBlock glyph, string fullText)
    {
        _clipboardService.CopyToClipboard(fullText);

        // Apple green visual confirmation
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x30, 0xD1, 0x58));
        chip.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x30, 0xD1, 0x58));
        glyph.Text = "\uE73E"; // Checkmark glyph
        glyph.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x30, 0xD1, 0x58));

        await Task.Delay(240);
        Finish(true);
    }

    private void QuickPick(int index)
    {
        var history = _clipboardService.History;
        if (index < 0 || index >= history.Count) return;

        string target = history[index];
        if (index < ItemsStack.Children.Count && ItemsStack.Children[index] is Border border &&
            border.Child is Grid grid && grid.Children.Count >= 3)
        {
            var chip = grid.Children[0] as Border;
            var actionBorder = grid.Children[2] as Border;
            var glyph = actionBorder?.Child as TextBlock;
            if (chip != null && glyph != null)
            {
                _ = TriggerCopyFeedbackAsync(border, chip, glyph, target);
                return;
            }
        }

        _clipboardService.CopyToClipboard(target);
        Finish(true);
    }

    private void OnClear()
    {
        _clipboardService.Clear();
        PopulateList();
    }

    private void Open()
    {
        if (!_anim) return;

        var d = TimeSpan.FromMilliseconds(320);
        var spring = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, d) { EasingFunction = spring });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, d) { EasingFunction = spring });
        CardShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, d) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Finish(bool ok)
    {
        if (_closing) return;
        _closing = true;

        if (!_anim)
        {
            DialogResult = ok;
            Close();
            return;
        }

        var d = TimeSpan.FromMilliseconds(130);
        var fade = new DoubleAnimation(0, d);
        fade.Completed += (_, _) =>
        {
            DialogResult = ok;
            Close();
        };

        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, d));
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, d));
        BeginAnimation(OpacityProperty, fade);
    }
}
