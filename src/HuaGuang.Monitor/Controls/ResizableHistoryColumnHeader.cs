namespace HuaGuang.Monitor.Controls;

/// <summary>历史表格表头：完整显示标题，右侧拖动手柄可调整列宽。</summary>
public sealed class ResizableHistoryColumnHeader : ContentView
{
    const double GripWidth = 10;
    const double MinColumnWidth = HuaGuang.Monitor.Services.HistoryTableFormatting.MinColumnWidth;

    readonly Label _label;
    readonly Grid _root;
    double _widthAtPanStart;

    public static readonly BindableProperty HeaderTextProperty =
        BindableProperty.Create(nameof(HeaderText), typeof(string), typeof(ResizableHistoryColumnHeader), string.Empty,
            propertyChanged: (bindable, _, value) =>
                ((ResizableHistoryColumnHeader)bindable)._label.Text = (string?)value ?? string.Empty);

    public static readonly BindableProperty ColumnWidthProperty =
        BindableProperty.Create(nameof(ColumnWidth), typeof(double), typeof(ResizableHistoryColumnHeader),
            HuaGuang.Monitor.Services.HistoryTableFormatting.TagColumnWidth,
            BindingMode.TwoWay,
            propertyChanged: (bindable, _, value) =>
                ((ResizableHistoryColumnHeader)bindable).ApplyWidth((double)value));

    public static readonly BindableProperty HeaderFontSizeProperty =
        BindableProperty.Create(nameof(HeaderFontSize), typeof(double), typeof(ResizableHistoryColumnHeader), 12d,
            propertyChanged: (bindable, _, value) =>
                ((ResizableHistoryColumnHeader)bindable)._label.FontSize = (double)value);

    public static readonly BindableProperty UseMonospaceFontProperty =
        BindableProperty.Create(nameof(UseMonospaceFont), typeof(bool), typeof(ResizableHistoryColumnHeader), false,
            propertyChanged: (bindable, _, value) =>
                ((ResizableHistoryColumnHeader)bindable).ApplyFont((bool)value));

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    public double HeaderFontSize
    {
        get => (double)GetValue(HeaderFontSizeProperty);
        set => SetValue(HeaderFontSizeProperty, value);
    }

    public bool UseMonospaceFont
    {
        get => (bool)GetValue(UseMonospaceFontProperty);
        set => SetValue(UseMonospaceFontProperty, value);
    }

    public ResizableHistoryColumnHeader()
    {
        _label = new Label
        {
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Color.FromArgb("#8AA0B5")
        };

        var grip = new BoxView
        {
            WidthRequest = GripWidth,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Color.FromArgb("#302EC4B6")
        };

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        grip.GestureRecognizers.Add(pan);

        _root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GripWidth)
            }
        };
        _root.Add(_label);
        _root.Add(grip, 1, 0);

        Content = _root;
    }

    void ApplyWidth(double width)
    {
        WidthRequest = width;
        _root.WidthRequest = width;
    }

    void ApplyFont(bool monospace) =>
        _label.FontFamily = monospace ? "Consolas" : null;

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _widthAtPanStart = ColumnWidth;
                break;
            case GestureStatus.Running:
                ColumnWidth = Math.Max(MinColumnWidth, _widthAtPanStart + e.TotalX);
                break;
        }
    }
}
