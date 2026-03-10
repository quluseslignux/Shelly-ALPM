using GObject;
using Gtk;
using Shelly.Gtk.Helpers;
using Shelly.Gtk.Services;
using Shelly.Gtk.UiModels;
using Shelly.Gtk.UiModels.PackageManagerObjects;
using Shelly.Gtk.UiModels.PackageManagerObjects.GObjects;

// ReSharper disable CollectionNeverQueried.Local

namespace Shelly.Gtk.Windows.Packages;

public class PackageInstall(
    IPrivilegedOperationService privilegedOperationService,
    ILockoutService lockoutService,
    IConfigService configService,
    IGenericQuestionService genericQuestionService)
    : IShellyWindow
{
    private Overlay _overlay = null!;
    private readonly CancellationTokenSource _cts = new();
    private ColumnView _columnView = null!;
    private SingleSelection _selectionModel = null!;
    private Gio.ListStore _listStore = null!;
    private TreeListModel _treeListModel = null!;
    private FilterListModel _filterListModel = null!;
    private CustomFilter _filter = null!;
    private string _searchText = string.Empty;
    private List<AlpmPackageDto> _packages = [];

    private Dictionary<ListItem, (SignalHandler<CheckButton> OnToggled, EventHandler OnExternalToggle)> _checkBinding =
        [];

    private SignalListItemFactory _checkFactory = null!;
    private SignalListItemFactory _nameFactory = null!;
    private SignalListItemFactory _sizeFactory = null!;
    private SignalListItemFactory _versionFactory = null!;
    private SignalListItemFactory _repositoryFactory = null!;
    private readonly List<AlpmPackageGObject> _packageGObjectRefs = [];
    private bool _disposed;
    private readonly List<GObject.Object> _childModelRefs = [];

    private Button _installButton = null!;
    private Button _localInstallButton = null!;
    private Button _appImageButton = null!;
    private SearchEntry _searchEntry = null!;
    private Builder _builder = null!;
    private ColumnViewColumn _checkColumn = null!;
    private ColumnViewColumn _nameColumn = null!;
    private ColumnViewColumn _sizeColumn = null!;
    private ColumnViewColumn _versionColumn = null!;
    private ColumnViewColumn _repositoryColumn = null!;

    public Widget CreateWindow()
    {
        _builder = Builder.NewFromString(ResourceHelper.LoadUiFile("UiFiles/Package/PackageWindow.ui"), -1);
        _overlay = (Overlay)_builder.GetObject("PackageWindow")!;
        _columnView = (ColumnView)_builder.GetObject("package_column_view")!;
        _checkColumn = (ColumnViewColumn)_builder.GetObject("check_column")!;
        _nameColumn = (ColumnViewColumn)_builder.GetObject("name_column")!;
        _sizeColumn = (ColumnViewColumn)_builder.GetObject("size_column")!;
        _versionColumn = (ColumnViewColumn)_builder.GetObject("version_column")!;
        _repositoryColumn = (ColumnViewColumn)_builder.GetObject("repository_column")!;
        _installButton = (Button)_builder.GetObject("install_button")!;
        _localInstallButton = (Button)_builder.GetObject("install_local_button")!;
        _appImageButton = (Button)_builder.GetObject("install_appimage_button")!;
        _searchEntry = (SearchEntry)_builder.GetObject("search_entry")!;
        _listStore = Gio.ListStore.New(AlpmPackageGObject.GetGType());
        _treeListModel = TreeListModel.New(_listStore, false, false, CreateChildModel);
        _filter = CustomFilter.New(FilterPackage);
        _filterListModel = FilterListModel.New(_treeListModel, _filter);
        _selectionModel = SingleSelection.New(_filterListModel);
        _selectionModel.CanUnselect = true;
        _columnView.SetModel(_selectionModel);
        SetupColumns(_checkColumn, _nameColumn, _sizeColumn, _versionColumn, _repositoryColumn);

        ColumnViewHelper.AlignColumnHeader(_columnView, 1, Align.End);
        ColumnViewHelper.AlignColumnHeader(_columnView, 2, Align.End);
        ColumnViewHelper.AlignColumnHeader(_columnView, 3, Align.End);

        _columnView.OnRealize += (_, _) => { _ = LoadDataAsync(_cts.Token); };
        _columnView.OnActivate += (_, _) =>
        {
            var item = _selectionModel.GetSelectedItem();
            if (item is TreeListRow row)
            {
                if (row.GetItem() is AlpmPackageGObject pkgObj)
                {
                    pkgObj.ToggleSelection();
                }

                row.SetExpanded(!row.GetExpanded());
            }
        };
        _searchEntry.OnSearchChanged += (_, _) =>
        {
            _searchText = _searchEntry.GetText();
            ApplyFilter();
        };
        _installButton.OnClicked += (_, _) => { _ = InstallSelectedAsync(); };
        _localInstallButton.OnClicked += (_, _) => { _ = InstallLocalPackage(); };
        _appImageButton.OnClicked += (_, _) => { _ = InstallAppImage(); };

        return _overlay;
    }

    private void SetupColumns(ColumnViewColumn checkColumn, ColumnViewColumn nameColumn,
        ColumnViewColumn sizeColumn, ColumnViewColumn versionColumn, ColumnViewColumn repositoryColumn)
    {
        _checkFactory = SignalListItemFactory.New();
        //TODO: INTERMITTENT CAST ERROR HERE
        _checkFactory.OnSetup += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var check = new CheckButton { MarginStart = 10, MarginEnd = 10 };
            listItem.SetChild(check);
        };

        _checkFactory.OnBind += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var row = listItem.GetItem() as TreeListRow;
            if (row?.GetItem() is not AlpmPackageGObject pkgObj ||
                listItem.GetChild() is not CheckButton checkButton) return;

            checkButton.SetActive(pkgObj.IsSelected);
            checkButton.Visible = true;
            checkButton.OnToggled += OnToggled;

            pkgObj.OnSelectionToggled += OnExternalToggle;
            _checkBinding[listItem] = (OnToggled, OnExternalToggle);

            return;

            void OnToggled(CheckButton s, EventArgs e)
            {
                pkgObj.IsSelected = s.GetActive();
            }

            void OnExternalToggle(object? s, EventArgs e)
            {
                checkButton.SetActive(pkgObj.IsSelected);
            }
        };

        _checkFactory.OnUnbind += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var row = listItem.GetItem() as TreeListRow;
            if (row?.GetItem() is not AlpmPackageGObject pkgObj ||
                listItem.GetChild() is not CheckButton checkButton) return;
            if (_checkBinding.Remove(listItem, out var handlers))
            {
                pkgObj.OnSelectionToggled -= handlers.OnExternalToggle;
                checkButton.OnToggled -= handlers.OnToggled;
            }
        };

        checkColumn.SetFactory(_checkFactory);

        _nameFactory = SignalListItemFactory.New();
        _nameFactory.OnSetup += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var expander = TreeExpander.New();
            var box = Box.New(Orientation.Horizontal, 6);
            var label = Label.New(string.Empty);
            var installedIcon = Image.NewFromIconName("object-select-symbolic");

            box.Append(label);
            box.Append(installedIcon);
            expander.SetChild(box);
            listItem.SetChild(expander);
        };
        _nameFactory.OnBind += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var row = listItem.GetItem() as TreeListRow;
            if (row == null || listItem.GetChild() is not TreeExpander expander) return;

            expander.SetListRow(row);
            var box = (Box)expander.GetChild()!;
            var label = (Label)box.GetFirstChild()!;
            var installedIcon = (Image)label.GetNextSibling()!;

            var item = row.GetItem();
            if (item is AlpmPackageGObject { Package: { } pkg })
            {
                label.SetText(pkg.Name);
                label.Halign = Align.Start;
                installedIcon.Visible = ((AlpmPackageGObject)item).IsInstalled;
                installedIcon.TooltipText = "Installed";
            }
            else if (item is StringObject strObj)
            {
                label.SetText(strObj.GetString());
                label.Halign = Align.Start;
                installedIcon.Visible = false;
            }
        };
        nameColumn.SetFactory(_nameFactory);

        _sizeFactory = SignalListItemFactory.New();
        _sizeFactory.OnSetup += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var label = Label.New(string.Empty);
            listItem.SetChild(label);
        };
        _sizeFactory.OnBind += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var row = listItem.GetItem() as TreeListRow;
            if (row?.GetItem() is not AlpmPackageGObject { Package: { } pkg } ||
                listItem.GetChild() is not Label label) return;
            label.SetText(SizeHelpers.FormatSize(pkg.InstalledSize));
            label.Halign = Align.End;
        };
        sizeColumn.SetFactory(_sizeFactory);

        _versionFactory = SignalListItemFactory.New();
        _versionFactory.OnSetup += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var label = Label.New(string.Empty);
            listItem.SetChild(label);
        };
        _versionFactory.OnBind += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var row = listItem.GetItem() as TreeListRow;
            if (row?.GetItem() is not AlpmPackageGObject { Package: { } pkg } ||
                listItem.GetChild() is not Label label) return;
            label.SetText(pkg.Version);
            label.Halign = Align.End;
        };
        versionColumn.SetFactory(_versionFactory);

        _repositoryFactory = new SignalListItemFactory();
        _repositoryFactory.OnSetup += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var label = Label.New(string.Empty);
            listItem.SetChild(label);
        };

        _repositoryFactory.OnBind += (_, args) =>
        {
            var listItem = (ListItem)args.Object;
            var row = listItem.GetItem() as TreeListRow;
            if (row?.GetItem() is not AlpmPackageGObject { Package: { } pkg } ||
                listItem.GetChild() is not Label label) return;
            label.SetText(pkg.Repository);
            label.Halign = Align.End;
        };
        repositoryColumn.SetFactory(_repositoryFactory);
    }


    public void Dispose()
    {
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();

        // 1. Disconnect model from view
        _columnView.SetModel(null);

        // 2. Clear columns' factories
        _checkColumn.SetFactory(null);
        _nameColumn.SetFactory(null);
        _sizeColumn.SetFactory(null);
        _versionColumn.SetFactory(null);
        _repositoryColumn.SetFactory(null);

        // 3. Remove all items from the store
        _listStore.RemoveAll();

        // 4. Dispose model chain (non-widget GObjects)
        _selectionModel.Dispose();
        _filterListModel.Dispose();
        _filter.Dispose();
        _treeListModel.Dispose();
        _listStore.Dispose();

        // 5. Dispose factories (non-widget GObjects)
        _checkFactory.Dispose();
        _nameFactory.Dispose();
        _sizeFactory.Dispose();
        _versionFactory.Dispose();
        _repositoryFactory.Dispose();

        // 6. DO NOT dispose widgets — just null out references
        //    GTK owns these via the widget tree and will clean them up
        _checkColumn = null!;
        _nameColumn = null!;
        _sizeColumn = null!;
        _versionColumn = null!;
        _repositoryColumn = null!;
        _installButton = null!;
        _localInstallButton = null!;
        _appImageButton = null!;
        _searchEntry = null!;
        _columnView = null!;
        _overlay = null!;
        _builder = null!;

        // 7. Clear managed collections
        _checkBinding.Clear();
        _checkBinding = null!;
        _packages = null!;
        _childModelRefs.Clear();
        _packageGObjectRefs.Clear();
    }

    private async Task LoadDataAsync(CancellationToken ct = default)
    {
        _listStore.RemoveAll();
        _packageGObjectRefs.Clear();
        _childModelRefs.Clear();
        try
        {
            _packages = await privilegedOperationService.GetAvailablePackagesAsync();
            var installedPackages = await privilegedOperationService.GetInstalledPackagesAsync();
            var installedNames = new HashSet<string>(installedPackages?.Select(x => x.Name) ?? []);

            ct.ThrowIfCancellationRequested();
            var queue = new Queue<AlpmPackageDto>(_packages);

            GLib.Functions.IdleAdd(0, () =>
            {
                if (ct.IsCancellationRequested || _disposed)
                {
                    return false;
                }

                // This number might need to be adjusted based on cpu. This comment is just so we can find this later
                // when we inevitably get a bug report about the package page being slow.
                const int batchSize = 1000;
                var count = 0;
                var batch = new List<AlpmPackageGObject>();
                while (queue.Count > 0 && count < batchSize)
                {
                    var dequeued = queue.Dequeue();
                    var pkgObj = new AlpmPackageGObject()
                        { Package = dequeued, IsInstalled = installedNames.Contains(dequeued.Name) };
                    _packageGObjectRefs.Add(pkgObj); // prevent GC
                    batch.Add(pkgObj);
                    count++;
                }

                // ReSharper disable once CoVariantArrayConversion
                _listStore.Splice(_listStore.GetNItems(), 0, batch.ToArray(), (uint)batch.Count);

                return queue.Count > 0;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to load packages: {e.Message}");
        }
    }

    private void ApplyFilter()
    {
        _filter.Changed(FilterChange.Different);
    }

    private bool FilterPackage(GObject.Object obj)
    {
        if (obj is TreeListRow row)
        {
            var item = row.GetItem();
            if (item is AlpmPackageGObject pkgObj && pkgObj.Package != null)
            {
                if (string.IsNullOrWhiteSpace(_searchText))
                    return true;

                return pkgObj.Package.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                       pkgObj.Package.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
            }

            // Always show detail (child) rows if their parent is visible
            return true;
        }

        return false;
    }

    private Gio.ListModel? CreateChildModel(GObject.Object item)
    {
        if (item is not AlpmPackageGObject { Package: { } pkg })
            return null;

        var details = Gio.ListStore.New(StringObject.GetGType());
        _childModelRefs.Add(details);

        void AddDetail(string text)
        {
            var so = StringObject.New(text);
            _childModelRefs.Add(so); // prevent GC
            details.Append(so);
        }

        AddDetail($"Description: {pkg.Description}");
        if (pkg.Depends.Count > 0)
            AddDetail($"Depends: {string.Join(", ", pkg.Depends)}");
        if (pkg.Licenses.Count > 0)
            AddDetail($"Licenses: {string.Join(", ", pkg.Licenses)}");
        if (!string.IsNullOrEmpty(pkg.Url))
            AddDetail($"URL: {pkg.Url}");
        if (pkg.OptDepends.Count > 0)
            AddDetail($"Optional Deps: {string.Join(", ", pkg.OptDepends)}");
        if (pkg.Provides.Count > 0)
            AddDetail($"Provides: {string.Join(", ", pkg.Provides)}");
        if (pkg.Conflicts.Count > 0)
            AddDetail($"Conflicts: {string.Join(", ", pkg.Conflicts)}");
        if (pkg.Groups.Count > 0)
            AddDetail($"Groups: {string.Join(", ", pkg.Groups)}");
        return details;
    }

    private async Task InstallSelectedAsync()
    {
        var selectedPackages = new List<string>();
        for (uint i = 0; i < _listStore.GetNItems(); i++)
        {
            var item = _listStore.GetObject(i);
            if (item is AlpmPackageGObject { IsSelected: true, Package: not null } pkgObj)
            {
                selectedPackages.Add(pkgObj.Package.Name);
            }
        }

        if (selectedPackages.Count != 0)
        {
            try
            {
                if (!configService.LoadConfig().NoConfirm)
                {
                    var args = new GenericQuestionEventArgs(
                        "Install Packages?", string.Join("\n", selectedPackages)
                    );

                    genericQuestionService.RaiseQuestion(args);
                    if (!await args.ResponseTask)
                    {
                        return;
                    }
                }

                lockoutService.Show($"Installing...");
                await privilegedOperationService.InstallPackagesAsync(selectedPackages);
                await LoadDataAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to install packages: {e.Message}");
            }
            finally
            {
                lockoutService.Hide();
            }
        }
    }

    private async Task InstallLocalPackage()
    {
        try
        {
            var dialog = FileDialog.New();
            dialog.SetTitle("Install Local Package");

            var filter = FileFilter.New();
            filter.SetName("Local package files (\"*.xz\", \"*.gz\", \"*.zst\")");
            filter.AddPattern("*.xz");
            filter.AddPattern("*.gz");
            filter.AddPattern("*.zst");

            var filters = Gio.ListStore.New(FileFilter.GetGType());
            filters.Append(filter);
            dialog.SetFilters(filters);

            var file = await dialog.OpenAsync((Window)_overlay.GetRoot()!);

            if (file is not null)
            {
                lockoutService.Show($"Installing local package...");
                var result = await privilegedOperationService.InstallLocalPackageAsync(file.GetPath()!);
                if (!result.Success)
                {
                    Console.WriteLine($"Failed to install local package: {result.Error}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to install local package: {ex.Message}");
        }
        finally
        {
            lockoutService.Hide();
        }
    }

    private async Task InstallAppImage()
    {
        try
        {
            var dialog = FileDialog.New();
            dialog.SetTitle("Install App Image");

            var filter = FileFilter.New();
            filter.SetName("Local AppImage files (\"*.AppImage\"");
            filter.AddPattern("*.AppImage");

            var filters = Gio.ListStore.New(FileFilter.GetGType());
            filters.Append(filter);
            dialog.SetFilters(filters);

            var file = await dialog.OpenAsync((Window)_overlay.GetRoot()!);

            if (file is not null)
            {
                lockoutService.Show($"Installing AppImage...");
                var result = await privilegedOperationService.InstallAppImageAsync(file.GetPath()!);
                if (!result.Success)
                {
                    Console.WriteLine($"Failed to install local package: {result.Error}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to install local package: {ex.Message}");
        }
        finally
        {
            lockoutService.Hide();
        }
    }
}