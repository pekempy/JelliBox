using JelliBox.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace JelliBox.Controls;

public class HomeSectionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ContinueWatchingTemplate { get; set; }
    public DataTemplate? LibrarySectionTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => item is HomeSectionRow { IsContinueWatching: true }
            ? ContinueWatchingTemplate ?? base.SelectTemplateCore(item, container)
            : LibrarySectionTemplate ?? base.SelectTemplateCore(item, container);
}
