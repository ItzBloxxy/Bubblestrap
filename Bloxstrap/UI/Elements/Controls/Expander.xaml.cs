using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Wpf.Ui.Common;

namespace Bloxstrap.UI.Elements.Controls
{
    /// <summary>
    /// Interaction logic for Expander.xaml
    /// </summary>
    [ContentProperty(nameof(InnerContent))]
    public partial class Expander : UserControl
    {
        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(Expander));

        public static readonly DependencyProperty HeaderIconProperty =
            DependencyProperty.Register(nameof(HeaderIcon), typeof(SymbolRegular), typeof(Expander));

        public static readonly DependencyProperty HeaderTextProperty =
            DependencyProperty.Register(nameof(HeaderText), typeof(string), typeof(Expander));

        public static readonly DependencyProperty InnerContentProperty =
            DependencyProperty.Register(nameof(InnerContent), typeof(object), typeof(Expander));

        public bool IsExpanded
        {
            get { return (bool)GetValue(IsExpandedProperty); }
            set { SetValue(IsExpandedProperty, value); }
        }

        public string HeaderText
        {
            get { return (string)GetValue(HeaderTextProperty); }
            set { SetValue(HeaderTextProperty, value); }
        }

        public SymbolRegular HeaderIcon
        {
            get { return (SymbolRegular)GetValue(HeaderIconProperty); }
            set { SetValue(HeaderTextProperty, value); }
        }

        public object InnerContent
        {
            get { return GetValue(InnerContentProperty); }
            set { SetValue(InnerContentProperty, value); }
        }

        public Expander()
        {
            InitializeComponent();
        }
    }
}
