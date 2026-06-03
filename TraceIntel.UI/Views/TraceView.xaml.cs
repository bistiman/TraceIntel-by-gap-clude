using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TraceIntel.UI.ViewModels;

namespace TraceIntel.UI.Views
{
    public partial class TraceView : UserControl
    {
        public TraceView()
        {
            InitializeComponent();
            DataContextChanged += TraceView_DataContextChanged;
        }

        private void TraceView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is TraceViewModel vm)
            {
                WireRoutingColumns(vm);
            }
        }

        private void WireRoutingColumns(TraceViewModel vm)
        {
            BuildRoutingColumns(vm);

            vm.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName == nameof(TraceViewModel.HopColumnCount))
                {
                    BuildRoutingColumns(vm);
                }
            };

            vm.HopFilters.CollectionChanged += (_, __) => BuildRoutingColumns(vm);
        }

        private void BuildRoutingColumns(TraceViewModel vm)
        {
            if (RoutingDataGrid == null) return;

            RoutingDataGrid.Columns.Clear();

            // Fixed columns
            RoutingDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Domain",
                Binding = new Binding(nameof(RoutingRow.Domain)),
                ClipboardContentBinding = new Binding(nameof(RoutingRow.Domain)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 220,
                CanUserSort = true,
                SortMemberPath = nameof(RoutingRow.Domain)
            });

            RoutingDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Destination",
                Binding = new Binding(nameof(RoutingRow.Destination)),
                ClipboardContentBinding = new Binding(nameof(RoutingRow.Destination)),
                Width = new DataGridLength(180),
                CanUserSort = true,
                SortMemberPath = nameof(RoutingRow.Destination)
            });

            RoutingDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Resolved",
                Binding = new Binding(nameof(RoutingRow.ResolvedDestination)),
                ClipboardContentBinding = new Binding(nameof(RoutingRow.ResolvedDestination)),
                Width = new DataGridLength(260),
                CanUserSort = true,
                SortMemberPath = nameof(RoutingRow.ResolvedDestination)
            });

            // Dynamic hop columns
            var headerTemplate = (DataTemplate)Resources["HopColumnHeaderTemplate"];
            var cellTemplate = (DataTemplate)Resources["HopCellTemplate"];
            var cellTemplatePrimary = (DataTemplate)Resources["HopCellTemplatePrimary"];

            for (var hop = 1; hop <= vm.HopColumnCount; hop++)
            {
                var hopIndex = hop - 1;
                object header = hop <= vm.HopFilters.Count ? vm.HopFilters[hopIndex] : $"Hop {hop}";
                var templateToUse = hop <= 5 ? cellTemplatePrimary : cellTemplate;

                var col = new DataGridTemplateColumn
                {
                    Header = header,
                    HeaderTemplate = headerTemplate,
                    Width = new DataGridLength(hop <= 5 ? 170 : 140),
                    CanUserSort = true,
                    SortMemberPath = $"Hops[{hopIndex}].IP",
                    ClipboardContentBinding = new Binding($"Hops[{hopIndex}].IP")
                };

                col.CellTemplate = new DataTemplate
                {
                    VisualTree = CreateHopCellFactory(templateToUse, hopIndex)
                };

                RoutingDataGrid.Columns.Add(col);
            }
        }

        private static FrameworkElementFactory CreateHopCellFactory(DataTemplate hopCellTemplate, int hopIndex)
        {
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty, hopCellTemplate);
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding($"Hops[{hopIndex}]"));
            return presenter;
        }

        private void CopyWithHeaders_Click(object sender, RoutedEventArgs e)
        {
            if (RoutingDataGrid == null) return;

            var oldMode = RoutingDataGrid.ClipboardCopyMode;
            try
            {
                RoutingDataGrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
                System.Windows.Input.ApplicationCommands.Copy.Execute(null, RoutingDataGrid);
            }
            finally
            {
                RoutingDataGrid.ClipboardCopyMode = oldMode;
            }
        }
    }
}
