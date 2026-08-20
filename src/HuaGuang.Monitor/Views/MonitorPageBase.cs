namespace HuaGuang.Monitor.Views;

public class MonitorPageBase : ContentPage
{
    public static readonly BindableProperty IsLandscapeProperty =
        BindableProperty.Create(nameof(IsLandscape), typeof(bool), typeof(MonitorPageBase), false);

    public static readonly BindableProperty IsCompactLayoutProperty =
        BindableProperty.Create(nameof(IsCompactLayout), typeof(bool), typeof(MonitorPageBase), false);

    public static readonly BindableProperty IsExpandedLayoutProperty =
        BindableProperty.Create(nameof(IsExpandedLayout), typeof(bool), typeof(MonitorPageBase), false);

    public static readonly BindableProperty ShowCompactInfoPanelProperty =
        BindableProperty.Create(nameof(ShowCompactInfoPanel), typeof(bool), typeof(MonitorPageBase), false);

    public static readonly BindableProperty InfoPanelMaxHeightProperty =
        BindableProperty.Create(nameof(InfoPanelMaxHeight), typeof(double), typeof(MonitorPageBase), 200.0);

    public static readonly BindableProperty TitleFontSizeProperty =
        BindableProperty.Create(nameof(TitleFontSize), typeof(double), typeof(MonitorPageBase), 20.0);

    public static readonly BindableProperty SubtitleFontSizeProperty =
        BindableProperty.Create(nameof(SubtitleFontSize), typeof(double), typeof(MonitorPageBase), 12.0);

    public static readonly BindableProperty BodyFontSizeProperty =
        BindableProperty.Create(nameof(BodyFontSize), typeof(double), typeof(MonitorPageBase), 12.0);

    public static readonly BindableProperty ValueFontSizeProperty =
        BindableProperty.Create(nameof(ValueFontSize), typeof(double), typeof(MonitorPageBase), 22.0);

    public static readonly BindableProperty CaptionFontSizeProperty =
        BindableProperty.Create(nameof(CaptionFontSize), typeof(double), typeof(MonitorPageBase), 11.0);

    public static readonly BindableProperty SmallFontSizeProperty =
        BindableProperty.Create(nameof(SmallFontSize), typeof(double), typeof(MonitorPageBase), 10.0);

    public static readonly BindableProperty ButtonFontSizeProperty =
        BindableProperty.Create(nameof(ButtonFontSize), typeof(double), typeof(MonitorPageBase), 14.0);

    public static readonly BindableProperty CardMinHeightProperty =
        BindableProperty.Create(nameof(CardMinHeight), typeof(double), typeof(MonitorPageBase), 108.0);

    public static readonly BindableProperty CardMinWidthProperty =
        BindableProperty.Create(nameof(CardMinWidth), typeof(double), typeof(MonitorPageBase), 152.0);

    public static readonly BindableProperty CardPaddingProperty =
        BindableProperty.Create(nameof(CardPadding), typeof(Thickness), typeof(MonitorPageBase), new Thickness(8, 6));

    public bool IsLandscape
    {
        get => (bool)GetValue(IsLandscapeProperty);
        private set => SetValue(IsLandscapeProperty, value);
    }

    public bool IsCompactLayout
    {
        get => (bool)GetValue(IsCompactLayoutProperty);
        private set => SetValue(IsCompactLayoutProperty, value);
    }

    public bool IsExpandedLayout
    {
        get => (bool)GetValue(IsExpandedLayoutProperty);
        private set => SetValue(IsExpandedLayoutProperty, value);
    }

    public bool ShowCompactInfoPanel
    {
        get => (bool)GetValue(ShowCompactInfoPanelProperty);
        private set => SetValue(ShowCompactInfoPanelProperty, value);
    }

    public double InfoPanelMaxHeight
    {
        get => (double)GetValue(InfoPanelMaxHeightProperty);
        private set => SetValue(InfoPanelMaxHeightProperty, value);
    }

    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty);
        private set => SetValue(TitleFontSizeProperty, value);
    }

    public double SubtitleFontSize
    {
        get => (double)GetValue(SubtitleFontSizeProperty);
        private set => SetValue(SubtitleFontSizeProperty, value);
    }

    public double BodyFontSize
    {
        get => (double)GetValue(BodyFontSizeProperty);
        private set => SetValue(BodyFontSizeProperty, value);
    }

    public double ValueFontSize
    {
        get => (double)GetValue(ValueFontSizeProperty);
        private set => SetValue(ValueFontSizeProperty, value);
    }

    public double CaptionFontSize
    {
        get => (double)GetValue(CaptionFontSizeProperty);
        private set => SetValue(CaptionFontSizeProperty, value);
    }

    public double SmallFontSize
    {
        get => (double)GetValue(SmallFontSizeProperty);
        private set => SetValue(SmallFontSizeProperty, value);
    }

    public double ButtonFontSize
    {
        get => (double)GetValue(ButtonFontSizeProperty);
        private set => SetValue(ButtonFontSizeProperty, value);
    }

    public double CardMinHeight
    {
        get => (double)GetValue(CardMinHeightProperty);
        private set => SetValue(CardMinHeightProperty, value);
    }

    public double CardMinWidth
    {
        get => (double)GetValue(CardMinWidthProperty);
        private set => SetValue(CardMinWidthProperty, value);
    }

    public Thickness CardPadding
    {
        get => (Thickness)GetValue(CardPaddingProperty);
        private set => SetValue(CardPaddingProperty, value);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var landscape = width > height;
        var expanded = width >= 960 && height >= 540;
        var compact = !expanded && (height < 520 || (landscape && height < 640));

        IsLandscape = landscape;
        IsExpandedLayout = expanded;
        IsCompactLayout = compact;
        ShowCompactInfoPanel = expanded || (compact && landscape);
        InfoPanelMaxHeight = expanded ? 88 : compact ? 96 : 168;

        TitleFontSize = expanded ? 26 : compact ? 16 : 20;
        SubtitleFontSize = expanded ? 15 : compact ? 10 : 12;
        BodyFontSize = expanded ? 15 : compact ? 11 : 12;
        ValueFontSize = expanded ? 34 : compact ? 18 : 22;
        CaptionFontSize = expanded ? 13 : compact ? 10 : 11;
        SmallFontSize = expanded ? 12 : compact ? 9 : 10;
        ButtonFontSize = expanded ? 16 : compact ? 12 : 14;
        CardMinHeight = expanded ? 136 : compact ? 96 : 112;
        CardMinWidth = expanded ? 176 : compact ? 140 : 156;
        CardPadding = expanded ? new Thickness(12, 10) : compact ? new Thickness(6, 4) : new Thickness(8, 6);
    }
}
