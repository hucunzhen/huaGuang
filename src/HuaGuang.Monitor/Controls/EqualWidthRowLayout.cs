using System.Collections;
using System.Collections.Specialized;

namespace HuaGuang.Monitor.Controls;

/// <summary>
/// Single-row layout: each item gets an equal share of the available width.
/// </summary>
public sealed class EqualWidthRowLayout : ContentView
{
    readonly Grid _grid = new()
    {
        RowDefinitions = { new RowDefinition(GridLength.Auto) },
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Center
    };

    INotifyCollectionChanged? _subscribed;

    public EqualWidthRowLayout()
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        Content = _grid;
    }

    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(EqualWidthRowLayout), propertyChanged: OnItemsSourceChanged);

    public static readonly BindableProperty ItemTemplateProperty =
        BindableProperty.Create(nameof(ItemTemplate), typeof(DataTemplate), typeof(EqualWidthRowLayout), propertyChanged: OnItemsSourceChanged);

    public static readonly BindableProperty ItemSpacingProperty =
        BindableProperty.Create(nameof(ItemSpacing), typeof(double), typeof(EqualWidthRowLayout), 8d, propertyChanged: OnItemsSourceChanged);

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

    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    static void OnItemsSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var layout = (EqualWidthRowLayout)bindable;
        layout.DetachCollection(oldValue as INotifyCollectionChanged);
        layout.AttachCollection(newValue as INotifyCollectionChanged);
        layout.Rebuild();
    }

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

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

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
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(Rebuild);
            return;
        }

        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.ColumnSpacing = ItemSpacing;

        var template = ItemTemplate;
        var source = ItemsSource;
        if (template is null || source is null)
        {
            return;
        }

        var index = 0;
        foreach (var item in source)
        {
            var created = template.CreateContent();
            if (created is not View view)
            {
                continue;
            }

            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            view.BindingContext = item;
            view.HorizontalOptions = LayoutOptions.Fill;
            view.VerticalOptions = LayoutOptions.Fill;
            Grid.SetColumn(view, index);
            _grid.Children.Add(view);
            index++;
        }
    }
}
