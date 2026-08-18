using System.Collections;
using System.Collections.Specialized;

namespace HuaGuang.Monitor.Controls;

/// <summary>
/// Lays out all items in a star-sized grid that fills the available space,
/// so a small set of tiles is visible without scrolling.
/// </summary>
public class FitGrid : Grid
{
    const double Spacing = 8;

    readonly List<View> _itemViews = [];
    INotifyCollectionChanged? _subscribed;
    bool _rebuildQueued;
    int _lastColumns;
    int _lastRows;
    int _lastCount;

    public FitGrid()
    {
        ColumnSpacing = Spacing;
        RowSpacing = Spacing;
    }

    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(FitGrid),
            propertyChanged: OnItemsSourceChanged);

    public static readonly BindableProperty ItemTemplateProperty =
        BindableProperty.Create(
            nameof(ItemTemplate),
            typeof(DataTemplate),
            typeof(FitGrid),
            propertyChanged: OnRebuildNeeded);

    public static readonly BindableProperty EmptyViewProperty =
        BindableProperty.Create(
            nameof(EmptyView),
            typeof(View),
            typeof(FitGrid),
            propertyChanged: OnRebuildNeeded);

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

    static void OnItemsSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var grid = (FitGrid)bindable;
        grid.DetachCollection(oldValue as INotifyCollectionChanged);
        grid.AttachCollection(newValue as INotifyCollectionChanged);
        grid.QueueRebuild();
    }

    static void OnRebuildNeeded(BindableObject bindable, object? oldValue, object? newValue) =>
        ((FitGrid)bindable).QueueRebuild();

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
            Rebuild();
        });
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        Arrange(_itemViews.Count, width, height);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null)
        {
            DetachCollection(_subscribed);
        }
    }

    void Rebuild()
    {
        Children.Clear();
        _itemViews.Clear();
        _lastColumns = 0;
        _lastRows = 0;
        _lastCount = -1;

        var template = ItemTemplate;
        if (template is not null && ItemsSource is not null)
        {
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
                Children.Add(view);
            }
        }

        if (_itemViews.Count == 0 && EmptyView is { } empty)
        {
            if (empty.Parent is Layout parent)
            {
                parent.Remove(empty);
            }

            empty.HorizontalOptions = LayoutOptions.Center;
            empty.VerticalOptions = LayoutOptions.Center;
            Children.Add(empty);
            ColumnDefinitions.Clear();
            RowDefinitions.Clear();
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            RowDefinitions.Add(new RowDefinition(GridLength.Star));
            return;
        }

        Arrange(_itemViews.Count, Width, Height);
    }

    void Arrange(int count, double width, double height)
    {
        if (count <= 0)
        {
            return;
        }

        var columns = ChooseColumns(count, width, height);
        var rows = (int)Math.Ceiling(count / (double)columns);
        if (columns == _lastColumns && rows == _lastRows && count == _lastCount)
        {
            return;
        }

        _lastColumns = columns;
        _lastRows = rows;
        _lastCount = count;

        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        for (var i = 0; i < columns; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var i = 0; i < rows; i++)
        {
            RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }

        for (var i = 0; i < _itemViews.Count; i++)
        {
            var view = _itemViews[i];
            SetRow((BindableObject)view, i / columns);
            SetColumn((BindableObject)view, i % columns);
        }
    }

    internal static int ChooseColumns(int count, double width, double height)
    {
        if (count <= 1)
        {
            return 1;
        }

        if (width <= 0 || height <= 0)
        {
            return Math.Clamp((int)Math.Ceiling(Math.Sqrt(count)), 1, count);
        }

        var bestColumns = 1;
        var bestScore = double.NegativeInfinity;
        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (int)Math.Ceiling(count / (double)columns);
            var cellWidth = (width - Spacing * (columns - 1)) / columns;
            var cellHeight = (height - Spacing * (rows - 1)) / rows;
            if (cellWidth <= 1 || cellHeight <= 1)
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
            var tooSmall = (cellWidth < 110 ? 110 - cellWidth : 0)
                           + (cellHeight < 72 ? (72 - cellHeight) * 1.4 : 0);
            var score = 20 - aspectPenalty - wasted * 2 - tooSmall * 0.04;
            if (score > bestScore)
            {
                bestScore = score;
                bestColumns = columns;
            }
        }

        return bestColumns;
    }
}
