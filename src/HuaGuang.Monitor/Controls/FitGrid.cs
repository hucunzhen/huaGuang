using System.Collections;
using System.Collections.Specialized;

namespace HuaGuang.Monitor.Controls;

/// <summary>
/// Fills the viewport when tiles fit at minimum size; otherwise scrolls with a responsive grid.
/// </summary>
public class FitGrid : ContentView
{
    const double Spacing = 8;

    readonly Grid _fillGrid;
    readonly CollectionView _collectionView;
    readonly GridItemsLayout _layout;

    readonly List<View> _itemViews = [];
    INotifyCollectionChanged? _subscribed;
    bool _useFillMode;
    bool _rebuildQueued;
    int _lastColumns;
    int _lastRows;
    int _lastCount = -1;
    double _lastLayoutWidth = -1;
    double _lastLayoutHeight = -1;
    double _lastSpanWidth = -1;

    public FitGrid()
    {
        _fillGrid = new Grid
        {
            ColumnSpacing = Spacing,
            RowSpacing = Spacing
        };

        _layout = new GridItemsLayout(ItemsLayoutOrientation.Vertical)
        {
            Span = 2,
            HorizontalItemSpacing = Spacing,
            VerticalItemSpacing = Spacing
        };

        _collectionView = new CollectionView
        {
            ItemsLayout = _layout,
            SelectionMode = SelectionMode.None,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        Content = _collectionView;
    }

    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(FitGrid), propertyChanged: OnItemsSourceChanged);

    public static readonly BindableProperty ItemTemplateProperty =
        BindableProperty.Create(nameof(ItemTemplate), typeof(DataTemplate), typeof(FitGrid), propertyChanged: OnItemTemplateChanged);

    public static readonly BindableProperty EmptyViewProperty =
        BindableProperty.Create(nameof(EmptyView), typeof(View), typeof(FitGrid), propertyChanged: OnEmptyViewChanged);

    public static readonly BindableProperty MinItemWidthProperty =
        BindableProperty.Create(nameof(MinItemWidth), typeof(double), typeof(FitGrid), 152.0, propertyChanged: OnLayoutHintChanged);

    public static readonly BindableProperty MinItemHeightProperty =
        BindableProperty.Create(nameof(MinItemHeight), typeof(double), typeof(FitGrid), 104.0, propertyChanged: OnLayoutHintChanged);

    public static readonly BindableProperty CardPaddingProperty =
        BindableProperty.Create(nameof(CardPadding), typeof(Thickness), typeof(FitGrid), new Thickness(8, 6));

    public static readonly BindableProperty BodyFontSizeProperty =
        BindableProperty.Create(nameof(BodyFontSize), typeof(double), typeof(FitGrid), 12.0);

    public static readonly BindableProperty SubtitleFontSizeProperty =
        BindableProperty.Create(nameof(SubtitleFontSize), typeof(double), typeof(FitGrid), 12.0);

    public static readonly BindableProperty ValueFontSizeProperty =
        BindableProperty.Create(nameof(ValueFontSize), typeof(double), typeof(FitGrid), 22.0);

    public static readonly BindableProperty CaptionFontSizeProperty =
        BindableProperty.Create(nameof(CaptionFontSize), typeof(double), typeof(FitGrid), 11.0);

    public static readonly BindableProperty SmallFontSizeProperty =
        BindableProperty.Create(nameof(SmallFontSize), typeof(double), typeof(FitGrid), 10.0);

    public static readonly BindableProperty ButtonFontSizeProperty =
        BindableProperty.Create(nameof(ButtonFontSize), typeof(double), typeof(FitGrid), 14.0);

    public static readonly BindableProperty PreferFillProperty =
        BindableProperty.Create(nameof(PreferFill), typeof(bool), typeof(FitGrid), false, propertyChanged: OnLayoutHintChanged);

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public View? EmptyView
    {
        get => (View?)GetValue(EmptyViewProperty);
        set => SetValue(EmptyViewProperty, value);
    }

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double MinItemHeight
    {
        get => (double)GetValue(MinItemHeightProperty);
        set => SetValue(MinItemHeightProperty, value);
    }

    public Thickness CardPadding
    {
        get => (Thickness)GetValue(CardPaddingProperty);
        set => SetValue(CardPaddingProperty, value);
    }

    public double BodyFontSize
    {
        get => (double)GetValue(BodyFontSizeProperty);
        set => SetValue(BodyFontSizeProperty, value);
    }

    public double SubtitleFontSize
    {
        get => (double)GetValue(SubtitleFontSizeProperty);
        set => SetValue(SubtitleFontSizeProperty, value);
    }

    public double ValueFontSize
    {
        get => (double)GetValue(ValueFontSizeProperty);
        set => SetValue(ValueFontSizeProperty, value);
    }

    public double CaptionFontSize
    {
        get => (double)GetValue(CaptionFontSizeProperty);
        set => SetValue(CaptionFontSizeProperty, value);
    }

    public double SmallFontSize
    {
        get => (double)GetValue(SmallFontSizeProperty);
        set => SetValue(SmallFontSizeProperty, value);
    }

    public double ButtonFontSize
    {
        get => (double)GetValue(ButtonFontSizeProperty);
        set => SetValue(ButtonFontSizeProperty, value);
    }

    public bool PreferFill
    {
        get => (bool)GetValue(PreferFillProperty);
        set => SetValue(PreferFillProperty, value);
    }

    static void OnItemsSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var grid = (FitGrid)bindable;
        grid.DetachCollection(oldValue as INotifyCollectionChanged);
        grid.AttachCollection(newValue as INotifyCollectionChanged);
        grid.QueueRebuild();
    }

    static void OnRebuildNeeded(BindableObject bindable, object? oldValue, object? newValue) =>
        ((FitGrid)bindable).QueueRebuild();

    static void OnItemTemplateChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var grid = (FitGrid)bindable;
        grid._collectionView.ItemTemplate = (DataTemplate?)newValue;
        OnRebuildNeeded(bindable, oldValue, newValue);
    }

    static void OnEmptyViewChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var grid = (FitGrid)bindable;
        grid._collectionView.EmptyView = (View?)newValue;
        OnRebuildNeeded(bindable, oldValue, newValue);
    }

    static void OnLayoutHintChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((FitGrid)bindable).QueueLayoutUpdate();

    void AttachCollection(INotifyCollectionChanged? collection)
    {
        if (collection is null)
        {
            return;
        }

        collection.CollectionChanged += OnCollectionChanged;
        _subscribed = collection;
    }

    void DetachCollection(INotifyCollectionChanged? collection)
    {
        if (collection is null)
        {
            return;
        }

        collection.CollectionChanged -= OnCollectionChanged;
        if (ReferenceEquals(_subscribed, collection))
        {
            _subscribed = null;
        }
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRebuild();

    static int CountItems(IEnumerable? source)
    {
        if (source is null)
        {
            return 0;
        }

        var count = 0;
        foreach (var _ in source)
        {
            count++;
        }

        return count;
    }

    void QueueRebuild()
    {
        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _rebuildQueued = false;
            _lastColumns = 0;
            _lastRows = 0;
            _lastCount = -1;
            _lastLayoutWidth = -1;
            _lastLayoutHeight = -1;
            _lastSpanWidth = -1;
            RebuildItemViews();
            QueueLayoutUpdate();
        });
    }

    void QueueLayoutUpdate()
    {
        Dispatcher.Dispatch(() => UpdateLayout(Width, Height));
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateLayout(width, height);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null)
        {
            DetachCollection(_subscribed);
            _rebuildQueued = false;
            foreach (var view in _itemViews)
            {
                view.BindingContext = null;
                if (view.Parent is Layout parent)
                {
                    parent.Remove(view);
                }
            }

            _itemViews.Clear();
            _fillGrid.Children.Clear();
            _collectionView.ItemsSource = null;
        }
    }

    void RebuildItemViews()
    {
        foreach (var view in _itemViews)
        {
            if (view.Parent is Layout parent)
            {
                parent.Remove(view);
            }
        }

        _itemViews.Clear();
        _fillGrid.Children.Clear();

        var template = ItemTemplate;
        if (template is null || ItemsSource is null)
        {
            ShowEmptyInFillGrid();
            RefreshScrollItemsSource();
            return;
        }

        foreach (var item in ItemsSource)
        {
            var created = template.CreateContent();
            var view = created as View ?? (created as ViewCell)?.View;
            if (view is null)
            {
                continue;
            }

            view.BindingContext = item;
            view.HorizontalOptions = LayoutOptions.Fill;
            view.VerticalOptions = LayoutOptions.Fill;
            _itemViews.Add(view);
        }

        RefreshScrollItemsSource();
    }

    void RefreshScrollItemsSource()
    {
        _collectionView.ItemsSource = null;
        _collectionView.ItemsSource = ItemsSource;
    }

    void UpdateLayout(double width, double height)
    {
        var sourceCount = CountItems(ItemsSource);
        if (_itemViews.Count == 0 && sourceCount > 0)
        {
            QueueRebuild();
            return;
        }

        if (_itemViews.Count == 0)
        {
            ShowEmptyInFillGrid();
            RefreshScrollItemsSource();
            return;
        }

        if (width <= 0 || height <= 0)
        {
            ApplyScrollMode();
            UpdateSpan(width);
            return;
        }

        var minWidth = Math.Max(120, MinItemWidth);
        var minHeight = Math.Max(80, MinItemHeight);
        var columns = ChooseColumns(_itemViews.Count, width, height, minWidth, minHeight);
        var rows = (int)Math.Ceiling(_itemViews.Count / (double)columns);
        var cellWidth = (width - Spacing * (columns - 1)) / columns;
        var cellHeight = (height - Spacing * (rows - 1)) / rows;
        var canFill = cellWidth >= minWidth && cellHeight >= minHeight;
        if (!canFill && PreferFill && cellWidth >= minWidth && cellHeight >= 72)
        {
            canFill = true;
        }

        if (canFill)
        {
            ApplyFillMode(columns, rows, _itemViews.Count, width, height);
        }
        else
        {
            ApplyScrollMode();
            UpdateSpan(width);
        }
    }

    void ApplyFillMode(int columns, int rows, int count, double width, double height)
    {
        if (_useFillMode &&
            columns == _lastColumns &&
            rows == _lastRows &&
            count == _lastCount &&
            Math.Abs(width - _lastLayoutWidth) < 1 &&
            Math.Abs(height - _lastLayoutHeight) < 1)
        {
            return;
        }

        _useFillMode = true;
        _lastColumns = columns;
        _lastRows = rows;
        _lastCount = count;
        _lastLayoutWidth = width;
        _lastLayoutHeight = height;

        _fillGrid.ColumnDefinitions.Clear();
        _fillGrid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++)
        {
            _fillGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var i = 0; i < rows; i++)
        {
            _fillGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }

        _fillGrid.Children.Clear();
        for (var i = 0; i < _itemViews.Count; i++)
        {
            var view = _itemViews[i];
            if (view.Parent is Layout parent && !ReferenceEquals(parent, _fillGrid))
            {
                parent.Remove(view);
            }

            if (view.Parent is null)
            {
                _fillGrid.Children.Add(view);
            }

            Grid.SetRow(view, i / columns);
            Grid.SetColumn(view, i % columns);
        }

        if (!ReferenceEquals(Content, _fillGrid))
        {
            Content = _fillGrid;
        }
    }

    void ApplyScrollMode()
    {
        _useFillMode = false;
        _lastColumns = 0;
        _lastRows = 0;
        _lastCount = -1;
        _lastLayoutWidth = -1;
        _lastLayoutHeight = -1;
        _fillGrid.Children.Clear();

        _collectionView.ItemTemplate = ItemTemplate;
        _collectionView.EmptyView = EmptyView;
        RefreshScrollItemsSource();

        if (!ReferenceEquals(Content, _collectionView))
        {
            Content = _collectionView;
        }
        else
        {
            UpdateSpan(Width);
        }
    }

    void ShowEmptyInFillGrid()
    {
        _fillGrid.Children.Clear();
        _fillGrid.ColumnDefinitions.Clear();
        _fillGrid.RowDefinitions.Clear();
        _fillGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _fillGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        if (EmptyView is { } empty)
        {
            if (empty.Parent is Layout parent)
            {
                parent.Remove(empty);
            }

            empty.HorizontalOptions = LayoutOptions.Center;
            empty.VerticalOptions = LayoutOptions.Center;
            _fillGrid.Children.Add(empty);
        }

        _useFillMode = true;
        Content = _fillGrid;
    }

    void UpdateSpan(double width)
    {
        if (width <= 0)
        {
            return;
        }

        if (Math.Abs(width - _lastSpanWidth) < 1)
        {
            return;
        }

        _lastSpanWidth = width;
        var minWidth = Math.Max(120, MinItemWidth);
        var span = Math.Max(1, (int)((width + Spacing) / (minWidth + Spacing)));
        if (_layout.Span != span)
        {
            _layout.Span = span;
        }
    }

    internal static int ChooseColumns(int count, double width, double height) =>
        ChooseColumns(count, width, height, 152, 104);

    internal static int ChooseColumns(int count, double width, double height, double minItemWidth, double minItemHeight)
    {
        if (count <= 1)
        {
            return 1;
        }

        if (width <= 0 || height <= 0)
        {
            return Math.Clamp((int)Math.Ceiling(Math.Sqrt(count)), 1, count);
        }

        minItemWidth = Math.Max(120, minItemWidth);
        minItemHeight = Math.Max(80, minItemHeight);

        var bestColumns = 1;
        var bestScore = double.NegativeInfinity;
        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (int)Math.Ceiling(count / (double)columns);
            var cellWidth = (width - Spacing * (columns - 1)) / columns;
            var cellHeight = (height - Spacing * (rows - 1)) / rows;
            if (cellWidth < minItemWidth || cellHeight < minItemHeight)
            {
                continue;
            }

            var aspect = cellWidth / cellHeight;
            var aspectPenalty = aspect < 0.9
                ? (0.9 - aspect) * 2.5
                : aspect > 2.2
                    ? (aspect - 2.2) * 1.8
                    : 0;
            var wasted = (columns * rows - count) / (double)(columns * rows);
            var score = 20 - aspectPenalty - wasted * 2;
            if (score > bestScore)
            {
                bestScore = score;
                bestColumns = columns;
            }
        }

        if (bestScore > double.NegativeInfinity)
        {
            return bestColumns;
        }

        return Math.Max(1, Math.Min(count, (int)((width + Spacing) / (minItemWidth + Spacing))));
    }
}
