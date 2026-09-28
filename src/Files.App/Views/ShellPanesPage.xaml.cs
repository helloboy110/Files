// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Files.App.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Numerics;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI.ViewManagement;
using WinRT;
using GridSplitter = Files.App.Controls.GridSplitter;

namespace Files.App.Views
{
	/// <summary>
	/// Represents <see cref="Page"/> that holds multiple panes.
	/// </summary>
	public sealed partial class ShellPanesPage : Page, IShellPanesPage, ITabBarItemContent, INotifyPropertyChanged
	{
		// Dependency injections

		private IGeneralSettingsService GeneralSettingsService { get; } = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
		private IContentPageContext ContentPageContext { get; } = Ioc.Default.GetRequiredService<IContentPageContext>();
		private AppModel AppModel { get; } = Ioc.Default.GetRequiredService<AppModel>();

		// Constants

		private const string ShellBorderFocusOnState = "ShellBorderFocusOnState";
		private const string ShellBorderFocusOffState = "ShellBorderFocusOffState";
		private const string ShellBorderDualPaneOffState = "ShellBorderDualPaneOffState";

		/// <summary>
		/// Maximum number of panes; four panes fill the 2x2 grid (quad) layout.
		/// </summary>
		private const int MaxPaneCount = 4;

		// Fields

		private bool _wasRightPaneVisible;
		private int _savedPaneCount;
		private NavigationParams? _savedNavParamsRight;
		private readonly PointerEventHandler _panePointerPressedHandler;

		// Properties

		public bool IsLeftPaneActive
			=> ActivePane == (GetPane(0) as IShellPage);

		public bool IsRightPaneActive
			=> ActivePane == (GetPane(1) as IShellPage);

		public IFilesystemHelpers FilesystemHelpers
			=> ActivePane?.FilesystemHelpers!;

		public bool IsMultiPaneActive
			=> GetPaneCount() > 1;

		public bool IsMultiPaneAvailable
		{
			get
			{
				try
				{
					return !AppModel.IsMainWindowClosed && MainWindow.Instance.Bounds.Width > Constants.UI.MultiplePaneWidthThreshold;
				}
				catch
				{
					return false;
				}
			}
		}

		public IShellPage? ActivePaneOrColumn
		{
			get
			{
				// Active shell is column view
				if (ActivePane is not null && ActivePane.IsColumnView && ActivePane.SlimContentPage is ColumnsLayoutPage columnLayoutPage)
					return columnLayoutPage.ActiveColumnShellPage;

				return ActivePane ?? GetPane(0);
			}
		}

		private ShellPaneArrangement _ShellPaneArrangement;
		public ShellPaneArrangement ShellPaneArrangement
		{
			get => _ShellPaneArrangement;
			set
			{
				if (_ShellPaneArrangement != value)
				{
					_ShellPaneArrangement = value;
					ArrangePanes();

					// The quad layout always shows four panes
					if (value is ShellPaneArrangement.Grid)
						EnsureGridPanes();

					NotifyPropertyChanged(nameof(ShellPaneArrangement));
					Pane_ContentChanged(null, null!);
				}
			}
		}

		private bool _WindowIsCompact;
		public bool WindowIsCompact
		{
			get => _WindowIsCompact;
			set
			{
				if (value != _WindowIsCompact)
				{
					_WindowIsCompact = value;

					if (value)
					{
						// Close extra panes
						_wasRightPaneVisible = GetPaneCount() >= 2;
						_savedPaneCount = GetPaneCount();

						if (_wasRightPaneVisible)
						{
							var currentPath = GetPane(1)?.TabBarItemParameter?.NavigationParameter as string ?? "Home";
							_savedNavParamsRight = new NavigationParams { NavPath = currentPath };
							while (GetPaneCount() > 1)
								RemovePane(GetPaneCount() - 1);
						}
					}
					else if (_wasRightPaneVisible)
					{
						// Add back panes
						while (GetPaneCount() < Math.Min(_savedPaneCount, MaxPaneCount))
							AddPane();

						if (_savedNavParamsRight is not null)
						{
							NavParamsRight = _savedNavParamsRight;
							_savedNavParamsRight = null;
						}

						_wasRightPaneVisible = false;
					}

					NotifyPropertyChanged(nameof(IsMultiPaneAvailable));
				}
			}
		}

		private TabBarItemParameter? _TabBarItemParameter;
		public TabBarItemParameter? TabBarItemParameter
		{
			get => _TabBarItemParameter;
			set
			{
				if (_TabBarItemParameter != value)
				{
					_TabBarItemParameter = value;
					ContentChanged?.Invoke(this, value!);
				}
			}
		}

		private NavigationParams? _NavParamsLeft;
		public NavigationParams? NavParamsLeft
		{
			get => _NavParamsLeft;
			set
			{
				if (_NavParamsLeft != value)
				{
					_NavParamsLeft = value;
					NotifyPropertyChanged(nameof(NavParamsLeft));

					if (GetPane(0) is ModernShellPage page)
						page.NavParams = value!;
				}
			}
		}

		private NavigationParams? _NavParamsRight;
		public NavigationParams? NavParamsRight
		{
			get => _NavParamsRight;
			set
			{
				if (_NavParamsRight != value)
				{
					_NavParamsRight = value;
					NotifyPropertyChanged(nameof(NavParamsRight));

					if (GetPane(1) is ModernShellPage page)
						page.NavParams = value!;
				}
			}
		}

		private IShellPage? _ActivePane;
		public IShellPage? ActivePane
		{
			get => _ActivePane;
			set
			{
				if (_ActivePane != value)
				{
					_ActivePane = value;

					// Reset
					foreach (var pane in GetPanes())
						pane.IsCurrentInstance = false;

					if (ActivePane is not null)
						ActivePane.IsCurrentInstance = IsCurrentInstance;

					NotifyPropertyChanged(nameof(ActivePane));
					NotifyPropertyChanged(nameof(IsLeftPaneActive));
					NotifyPropertyChanged(nameof(IsRightPaneActive));
					NotifyPropertyChanged(nameof(ActivePaneOrColumn));
					NotifyPropertyChanged(nameof(FilesystemHelpers));

					SetShadow();
				}
			}
		}

		private bool _IsCurrentInstance;
		public bool IsCurrentInstance
		{
			get => _IsCurrentInstance;
			set
			{
				if (_IsCurrentInstance == value)
					return;

				_IsCurrentInstance = value;

				// Reset
				foreach (var pane in GetPanes())
					pane.IsCurrentInstance = false;

				if (ActivePane is not null)
				{
					ActivePane.IsCurrentInstance = value;

					if (value && ActivePane is BaseShellPage baseShellPage)
						baseShellPage.ContentPage?.ItemManipulationModel.FocusFileList();
				}

				CurrentInstanceChanged?.Invoke(null, this);
			}
		}


		// Events

		public static event EventHandler<ShellPanesPage>? CurrentInstanceChanged;
		public event EventHandler<TabBarItemParameter>? ContentChanged;
		public event PropertyChangedEventHandler? PropertyChanged;

		// Constructor

		public ShellPanesPage()
		{
			_panePointerPressedHandler = Pane_PointerPressed;
			InitializeComponent();

			ShellPaneArrangement = GeneralSettingsService.ShellPaneArrangementOption;

			// Initialize the default pane
			AddPane();

			// Set default values
			ActivePane = GetPane(0);

			try
			{
				_WindowIsCompact = MainWindow.Instance.Bounds.Width <= Constants.UI.MultiplePaneWidthThreshold;
				MainWindow.Instance.SizeChanged += MainWindow_SizeChanged;
			}
			catch (Exception)
			{
				// Handle exception in case WinUI Windows is closed
				// (see https://github.com/files-community/Files/issues/15599)

				_WindowIsCompact = false;
			}

			// Open the secondary pane
			if (IsMultiPaneAvailable &&
				GeneralSettingsService.AlwaysOpenDualPaneInNewTab)
				AddPane();

			// Fill the quad layout
			if (ShellPaneArrangement is ShellPaneArrangement.Grid && IsMultiPaneAvailable)
				EnsureGridPanes();

			TabBar.TabDragStarted += TabBar_TabDragStarted;
			TabBar.TabDragCompleted += TabBar_TabDragCompleted;
		}

		// Public methods

		/// <inheritdoc/>
		public void OpenSecondaryPane(string path = "", ShellPaneArrangement arrangement = ShellPaneArrangement.None)
		{
			if (GetPaneCount() <= 1)
				AddPane(arrangement is ShellPaneArrangement.None ? GeneralSettingsService.ShellPaneArrangementOption : arrangement);

			NavParamsRight = new() { NavPath = string.IsNullOrEmpty(path) ? "Home" : path };
		}

		/// <inheritdoc/>
		public void OpenInOtherPane(string path)
		{
			if (!IsMultiPaneActive || string.IsNullOrEmpty(path))
				return;

			var otherPane = GetNextPane(ActivePane);
			if (otherPane is null)
				return;

			otherPane.NavigateToPath(path);
			otherPane.Focus(FocusState.Programmatic);
		}

		/// <inheritdoc/>
		public void ArrangePanes(ShellPaneArrangement arrangement = ShellPaneArrangement.None)
		{
			if (arrangement is not ShellPaneArrangement.None)
				ShellPaneArrangement = arrangement;

			// Clear definitions
			RootGrid.RowDefinitions.Clear();
			RootGrid.ColumnDefinitions.Clear();

			var panes = GetPanes().ToList();
			var sizers = GetSizers().ToList();

			if (ShellPaneArrangement == ShellPaneArrangement.Grid && panes.Count >= 2)
			{
				// 2x2 grid: [pane | splitter] rows/columns with a cross of two splitters
				RootGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star), MinHeight = 100d });
				RootGrid.RowDefinitions.Add(new() { Height = new(4) });
				RootGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star), MinHeight = 100d });
				RootGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 100d });
				RootGrid.ColumnDefinitions.Add(new() { Width = new(4) });
				RootGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 100d });

				for (var i = 0; i < panes.Count && i < MaxPaneCount; i++)
				{
					panes[i].SetValue(Grid.RowProperty, i / 2 * 2);
					panes[i].SetValue(Grid.ColumnProperty, i % 2 * 2);
					panes[i].SetValue(Grid.RowSpanProperty, 1);
					panes[i].SetValue(Grid.ColumnSpanProperty, 1);
					panes[i].Visibility = Visibility.Visible;
				}

				for (var i = 0; i < sizers.Count; i++)
				{
					var splitter = sizers[i];
					if (i is 0)
					{
						// Vertical cross splitter
						splitter.SetValue(Grid.RowProperty, 0);
						splitter.SetValue(Grid.ColumnProperty, 1);
						splitter.SetValue(Grid.RowSpanProperty, 3);
						splitter.SetValue(Grid.ColumnSpanProperty, 1);
						splitter.Height = double.NaN;
						splitter.Width = 2;
						splitter.Visibility = Visibility.Visible;
					}
					else if (i is 1)
					{
						// Horizontal cross splitter
						splitter.SetValue(Grid.RowProperty, 1);
						splitter.SetValue(Grid.ColumnProperty, 0);
						splitter.SetValue(Grid.RowSpanProperty, 1);
						splitter.SetValue(Grid.ColumnSpanProperty, 3);
						splitter.Height = 2;
						splitter.Width = double.NaN;
						splitter.Visibility = Visibility.Visible;
					}
					else
					{
						splitter.Visibility = Visibility.Collapsed;
					}
				}
			}
			else if (ShellPaneArrangement == ShellPaneArrangement.Vertical)
			{
				foreach (var element in RootGrid.Children)
				{
					if (element is GridSplitter splitter)
					{
						RootGrid.ColumnDefinitions.Add(new() { Width = new(4) });
						splitter.Height = double.NaN;
						splitter.Width = 2;
					}
					else
					{
						RootGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 100d });
					}

					element.SetValue(Grid.ColumnProperty, RootGrid.ColumnDefinitions.Count - 1);
					element.SetValue(Grid.RowSpanProperty, 1);
					element.SetValue(Grid.ColumnSpanProperty, 1);
					element.Visibility = Visibility.Visible;
				}
			}
			else
			{
				foreach (var element in RootGrid.Children)
				{
					if (element is GridSplitter splitter)
					{
						RootGrid.RowDefinitions.Add(new() { Height = new(4) });
						splitter.Height = 2;
						splitter.Width = double.NaN;
					}
					else
					{
						RootGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star), MinHeight = 100d });
					}

					element.SetValue(Grid.RowProperty, RootGrid.RowDefinitions.Count - 1);
					element.SetValue(Grid.RowSpanProperty, 1);
					element.SetValue(Grid.ColumnSpanProperty, 1);
					element.Visibility = Visibility.Visible;
				}
			}

			// Update the default cursor type on hover based on pane arrangement
			var sizerList = GetSizers().ToList();
			for (var i = 0; i < sizerList.Count; i++)
			{
				var isHorizontalSplitter = ShellPaneArrangement is ShellPaneArrangement.Grid
					? i is 1
					: ShellPaneArrangement is not ShellPaneArrangement.Vertical;

				sizerList[i]?.ChangeCursor(
					InputSystemCursor.Create(
						isHorizontalSplitter
							? InputSystemCursorShape.SizeNorthSouth
							: InputSystemCursorShape.SizeWestEast));
			}
		}

		/// <inheritdoc/>
		public void CloseOtherPane()
		{
			if (!IsMultiPaneActive)
				return;

			var panes = GetPanes().ToList();
			var activeIndex = ActivePane is ModernShellPage active ? panes.IndexOf(active) : 0;
			var otherIndex = (activeIndex + 1) % panes.Count;
			RemovePane(otherIndex);
		}

		/// <inheritdoc/>
		public void CloseActivePane()
		{
			var panes = GetPanes().ToList();
			var activeIndex = ActivePane is ModernShellPage active ? panes.IndexOf(active) : 0;
			RemovePane(activeIndex < 0 ? 0 : activeIndex);

			GetPane(0)?.Focus(FocusState.Programmatic);
			SetShadow();
		}

		/// <inheritdoc/>
		public void FocusOtherPane()
		{
			if (!IsMultiPaneActive)
				return;

			ActivePane = GetNextPane(ActivePane);
			FocusActivePane();
		}

		/// <inheritdoc/>
		public void FocusActivePane()
		{
			var activePane = ActivePane is ModernShellPage pane && GetPanes().Contains(pane) ? pane : GetPane(0);

			// Skip when the file list is empty and no text input currently has focus:
			// focusing the pane in that state lands XAML focus on the outer ListView
			// element itself, which causes subsequent popup-dismissal to push newly
			// opened top-level windows behind main (Files #13697). If a text input
			// currently has focus (e.g. omnibar after Enter), we still need to move
			// focus off it so keyboard shortcuts work.
			if (activePane?.ShellViewModel?.FilesAndFolders?.Count == 0 &&
				!UIHelpers.IsTextInputFocused(activePane.XamlRoot))
				return;

			activePane?.Focus(FocusState.Programmatic);
			activePane?.ContentPage?.ItemManipulationModel.FocusFileList();
		}

		/// <inheritdoc/>
		public IEnumerable<ModernShellPage> GetPanes()
		{
			return RootGrid.Children.Where(x => RootGrid.Children.IndexOf(x) % 2 == 0).Cast<ModernShellPage>();
		}

		// Private methods

		private ModernShellPage? GetPane(int index = -1)
		{
			if (index is -1 || RootGrid.Children.Count - 1 < index)
				return null;

			var shellPage = RootGrid.Children[index * 2] as ModernShellPage;
			return shellPage;
		}

		private int GetPaneCount()
		{
			return (RootGrid.Children.Count + 1) / 2;
		}

		private ModernShellPage? GetNextPane(IShellPage? currentPane)
		{
			var panes = GetPanes().ToList();
			if (panes.Count is 0)
				return null;

			var index = currentPane is ModernShellPage pane ? panes.IndexOf(pane) : -1;
			return panes[(index + 1) % panes.Count];
		}

		private IEnumerable<GridSplitter> GetSizers()
		{
			return RootGrid.Children.Where(x => RootGrid.Children.IndexOf(x) % 2 == 1).Cast<GridSplitter>();
		}

		private void AddPane(ShellPaneArrangement arrangement = ShellPaneArrangement.None)
		{
			if (GetPaneCount() >= MaxPaneCount)
				return;

			if (arrangement is not ShellPaneArrangement.None)
				ShellPaneArrangement = arrangement;

			// Adding new pane is not the first time
			if (RootGrid.Children.Count is not 0)
				RootGrid.Children.Add(CreateSplitter());

			// Add new pane
			var page = CreatePane();
			RootGrid.Children.Add(page);

			// Rebuild all row/column definitions and grid positions
			ArrangePanes();

			// Focus
			ActivePane = GetPane(GetPaneCount() - 1);

			NotifyPropertyChanged(nameof(IsMultiPaneActive));
		}

		/// <summary>
		/// Adds panes until the 2x2 grid (quad) layout is filled with four panes.
		/// </summary>
		private void EnsureGridPanes()
		{
			if (RootGrid.Children.Count is 0)
				return;

			var added = false;
			while (GetPaneCount() < MaxPaneCount && IsMultiPaneAvailable)
			{
				RootGrid.Children.Add(CreateSplitter());
				RootGrid.Children.Add(CreatePane());
				added = true;
			}

			if (added)
			{
				ArrangePanes();
				NotifyPropertyChanged(nameof(IsMultiPaneActive));
			}
		}

		private GridSplitter CreateSplitter()
		{
			var sizer = new GridSplitter() { IsTabStop = false };
			sizer.DoubleTapped += Sizer_OnDoubleTapped;
			sizer.Loaded += Sizer_Loaded;
			sizer.ManipulationCompleted += Sizer_ManipulationCompleted;
			sizer.ManipulationStarted += Sizer_ManipulationStarted;
			return sizer;
		}

		private ModernShellPage CreatePane()
		{
			var page = new ModernShellPage() { PaneHolder = this };
			page.ContentChanged += Pane_ContentChanged;
			page.Loaded += Pane_Loaded;
			return page;
		}

		private void RemovePane(int index = -1)
		{
			if (index is -1 || GetPaneCount() <= index)
				return;

			// Remove the pane and its adjacent splitter
			var paneChildIndex = index * 2;
			var splitterChildIndex = paneChildIndex > 0 ? paneChildIndex - 1 : 0;
			RootGrid.Children.RemoveAt(paneChildIndex);
			if (RootGrid.Children.Count > 0 && RootGrid.Children[splitterChildIndex] is GridSplitter)
				RootGrid.Children.RemoveAt(splitterChildIndex);

			// Rebuild all row/column definitions and grid positions
			ArrangePanes();

			// Move focus when the active pane is removed
			if (ActivePane is null || !GetPanes().Contains(ActivePane))
				ActivePane = GetPane(Math.Max(GetPaneCount() - 1, 0));

			Pane_ContentChanged(null, null!);
			NotifyPropertyChanged(nameof(IsMultiPaneActive));
		}

		private void SetShadow()
		{
			if (IsMultiPaneActive)
			{
				// Add theme shadow to the active pane
				foreach (var pane in GetPanes())
				{
					var isActive = pane == ActivePane;
					pane.RootGrid.Translation = new System.Numerics.Vector3(0, 0, isActive ? 32 : 0);
					VisualStateManager.GoToState(pane, isActive ? ShellBorderFocusOnState : ShellBorderFocusOffState, true);
				}
			}
			else
			{
				if (GetPane(0) is ModernShellPage leftShellPage)
					leftShellPage.RootGrid.Translation = new System.Numerics.Vector3(0, 0, 8);

				VisualStateManager.GoToState(GetPane(0), ShellBorderDualPaneOffState, true);
			}
		}

		// Override methods

		protected override void OnNavigatedTo(NavigationEventArgs eventArgs)
		{
			base.OnNavigatedTo(eventArgs);

			if (eventArgs.Parameter is string navPath)
			{
				NavParamsLeft = new() { NavPath = navPath };
				NavParamsRight = new() { NavPath = "Home" };
			}
			else if (eventArgs.Parameter is PaneNavigationArguments paneArgs)
			{
				NavParamsLeft = new()
				{
					NavPath = paneArgs.LeftPaneNavPathParam,
					SelectItem = paneArgs.LeftPaneSelectItemParam
				};

				// Creates new pane
				if (GetPaneCount() is 1 &&
					IsMultiPaneAvailable &&
					paneArgs.RightPaneNavPathParam is not null)
					AddPane();

				NavParamsRight = new()
				{
					NavPath = paneArgs.RightPaneNavPathParam,
					SelectItem = paneArgs.RightPaneSelectItemParam
				};

				ShellPaneArrangement =
					paneArgs.ShellPaneArrangement is ShellPaneArrangement.None
						? ShellPaneArrangement.Vertical
						: paneArgs.ShellPaneArrangement;

				// Restore third/fourth panes of the 2x2 grid layout
				if (ShellPaneArrangement is ShellPaneArrangement.Grid && IsMultiPaneAvailable)
				{
					EnsureGridPanes();

					if (GetPane(2) is ModernShellPage thirdPane && !string.IsNullOrEmpty(paneArgs.ThirdPaneNavPathParam))
						thirdPane.NavParams = new() { NavPath = paneArgs.ThirdPaneNavPathParam };

					if (GetPane(3) is ModernShellPage fourthPane && !string.IsNullOrEmpty(paneArgs.FourthPaneNavPathParam))
						fourthPane.NavParams = new() { NavPath = paneArgs.FourthPaneNavPathParam };

					ActivePane = GetPane(0);
				}
			}

			TabBarItemParameter = new()
			{
				InitialPageType = typeof(ShellPanesPage),
				NavigationParameter = new PaneNavigationArguments()
				{
					LeftPaneNavPathParam = NavParamsLeft?.NavPath,
					LeftPaneSelectItemParam = NavParamsLeft?.SelectItem,
					RightPaneNavPathParam = GetPaneCount() >= 2 ? NavParamsRight?.NavPath : null,
					RightPaneSelectItemParam = GetPaneCount() >= 2 ? NavParamsRight?.SelectItem : null,
					ThirdPaneNavPathParam = GetPaneCount() >= 3 ? GetPane(2)?.TabBarItemParameter?.NavigationParameter as string : null,
					FourthPaneNavPathParam = GetPaneCount() >= 4 ? GetPane(3)?.TabBarItemParameter?.NavigationParameter as string : null,
					ShellPaneArrangement = ShellPaneArrangement,
				}
			};
		}

		// Event methods

		public Task TabItemDragOver(object sender, DragEventArgs e)
		{
			return ActivePane?.TabItemDragOver(sender, e) ?? Task.CompletedTask;
		}

		public Task TabItemDrop(object sender, DragEventArgs e)
		{
			return ActivePane?.TabItemDrop(sender, e) ?? Task.CompletedTask;
		}

		private TabBarItem? _draggedTabItem;

		private void TabBar_TabDragStarted(object? sender, TabBarItem? draggedItem)
		{
			if (!IsCurrentInstance ||
				draggedItem is null ||
				(draggedItem.NavigationParameter?.NavigationParameter is PaneNavigationArguments p && !string.IsNullOrEmpty(p.RightPaneNavPathParam)) ||
				GetPaneCount() != 1 ||
				!IsMultiPaneAvailable)
				return;

			_draggedTabItem = draggedItem;
			TabDropOverlay.Visibility = Visibility.Visible;
		}

		private void TabBar_TabDragCompleted(object? sender, TabBarItem? draggedItem)
		{
			TabDropOverlay.Visibility = Visibility.Collapsed;
			HideDropIndicator();
			_indicatorInitialized = false;
			_draggedTabItem = null;
		}

		private SpriteVisual? _indicatorVisual;
		private RectangleClip? _indicatorClip;
		private bool _indicatorInitialized;

		private static readonly string[] _cornerProps =
			["TopLeftRadius", "TopRightRadius", "BottomRightRadius", "BottomLeftRadius"];

		[DynamicWindowsRuntimeCast(typeof(SolidColorBrush))]
		private SpriteVisual GetOrCreateIndicatorVisual()
		{
			if (_indicatorVisual is not null)
				return _indicatorVisual;

			var compositor = ElementCompositionPreview.GetElementVisual(TabDropOverlay).Compositor;
			var accent = ((SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"]).Color;

			var sprite = compositor.CreateSpriteVisual();
			sprite.Brush = compositor.CreateColorBrush(accent);
			sprite.Opacity = 0f;
			sprite.AnchorPoint = new Vector2(0.5f, 0.5f);

			var clip = compositor.CreateRectangleClip();
			clip.StartAnimation("Right", BindToSize("X"));
			clip.StartAnimation("Bottom", BindToSize("Y"));
			sprite.Clip = clip;
			_indicatorClip = clip;

			if (new UISettings().AnimationsEnabled)
			{
				var anims = compositor.CreateImplicitAnimationCollection();
				anims["Offset"] = Tween(compositor.CreateVector3KeyFrameAnimation(), "Offset", 180);
				anims["Size"] = Tween(compositor.CreateVector2KeyFrameAnimation(), "Size", 180);
				anims["Opacity"] = Tween(compositor.CreateScalarKeyFrameAnimation(), "Opacity", 120);
				sprite.ImplicitAnimations = anims;

				var clipAnims = compositor.CreateImplicitAnimationCollection();
				foreach (var corner in _cornerProps)
					clipAnims[corner] = Tween(compositor.CreateVector2KeyFrameAnimation(), corner, 180);
				clip.ImplicitAnimations = clipAnims;
			}

			ElementCompositionPreview.SetElementChildVisual(TabDropOverlay, sprite);
			_indicatorVisual = sprite;
			return sprite;

			ExpressionAnimation BindToSize(string axis)
			{
				var anim = compositor.CreateExpressionAnimation($"v.Size.{axis}");
				anim.SetReferenceParameter("v", sprite);
				return anim;
			}

			static T Tween<T>(T anim, string target, int durationMs) where T : KeyFrameAnimation
			{
				anim.InsertExpressionKeyFrame(1f, "this.FinalValue");
				anim.Target = target;
				anim.Duration = TimeSpan.FromMilliseconds(durationMs);
				return anim;
			}
		}

		private void HideDropIndicator()
		{
			if (_indicatorVisual is not null)
				_indicatorVisual.Opacity = 0f;
		}

		private void ApplyZoneCorners(PaneDropZone zone)
		{
			if (_indicatorClip is null)
				return;

			var (tl, tr, br, bl) = zone switch
			{
				PaneDropZone.Left => (8f, 0f, 0f, 8f),
				PaneDropZone.Right => (0f, 8f, 8f, 0f),
				PaneDropZone.Top => (8f, 8f, 0f, 0f),
				_ => (0f, 0f, 8f, 8f),
			};
			_indicatorClip.TopLeftRadius = new Vector2(tl);
			_indicatorClip.TopRightRadius = new Vector2(tr);
			_indicatorClip.BottomRightRadius = new Vector2(br);
			_indicatorClip.BottomLeftRadius = new Vector2(bl);
		}

		private enum PaneDropZone { None, Left, Top, Right, Bottom }

		// Overlay diagonals carve it into four triangles; the cursor's triangle picks the edge to split toward.
		private PaneDropZone GetDropZone(DragEventArgs e)
		{
			var w = TabDropOverlay.ActualWidth;
			var h = TabDropOverlay.ActualHeight;
			if (w <= 0 || h <= 0)
				return PaneDropZone.None;

			var pos = e.GetPosition(TabDropOverlay);
			var nx = pos.X / w;
			var ny = pos.Y / h;

			if (ny < nx && ny < 1 - nx)
				return PaneDropZone.Top;
			if (ny > nx && ny > 1 - nx)
				return PaneDropZone.Bottom;
			return ny > nx ? PaneDropZone.Left : PaneDropZone.Right;
		}

		private void TabDropOverlay_DragOver(object sender, DragEventArgs e)
		{
			e.Handled = true;

			if (!e.DataView.Properties.ContainsKey(BaseTabBar.TabPathIdentifier))
			{
				HideDropIndicator();
				return;
			}

			var zone = GetDropZone(e);
			if (zone is PaneDropZone.None)
			{
				HideDropIndicator();
				e.AcceptedOperation = DataPackageOperation.None;
				return;
			}

			var w = (float)TabDropOverlay.ActualWidth;
			var h = (float)TabDropOverlay.ActualHeight;
			var vertical = zone is PaneDropZone.Left or PaneDropZone.Right;
			var size = vertical ? new Vector2(w / 2, h) : new Vector2(w, h / 2);
			var offset = zone switch
			{
				PaneDropZone.Left => new Vector3(w / 4, h / 2, 0),
				PaneDropZone.Right => new Vector3(3 * w / 4, h / 2, 0),
				PaneDropZone.Top => new Vector3(w / 2, h / 4, 0),
				_ => new Vector3(w / 2, 3 * h / 4, 0),
			};

			var visual = GetOrCreateIndicatorVisual();

			// Seed a zero-size state at the cursor with flat corners (no animation) so the assignment below grows out from there.
			if (!_indicatorInitialized)
			{
				var cursor = e.GetPosition(TabDropOverlay);
				var visualAnims = visual.ImplicitAnimations;
				var clipAnims = _indicatorClip!.ImplicitAnimations;
				visual.ImplicitAnimations = null;
				_indicatorClip.ImplicitAnimations = null;
				visual.Offset = new Vector3((float)cursor.X, (float)cursor.Y, 0);
				visual.Size = Vector2.Zero;
				_indicatorClip.TopLeftRadius = _indicatorClip.TopRightRadius =
					_indicatorClip.BottomRightRadius = _indicatorClip.BottomLeftRadius = Vector2.Zero;
				visual.ImplicitAnimations = visualAnims;
				_indicatorClip.ImplicitAnimations = clipAnims;
				_indicatorInitialized = true;
			}

			ApplyZoneCorners(zone);
			visual.Offset = offset;
			visual.Size = size;
			visual.Opacity = 0.35f;

			e.AcceptedOperation = DataPackageOperation.Move;
			e.DragUIOverride.Caption = (vertical
				? Strings.AddVerticalPaneDescription
				: Strings.SplitPaneHorizontallyDescription).GetLocalizedResource();
			e.DragUIOverride.IsCaptionVisible = true;
			e.DragUIOverride.IsGlyphVisible = false;
		}

		private void TabDropOverlay_DragLeave(object sender, DragEventArgs e)
			=> HideDropIndicator();

		private void TabDropOverlay_Drop(object sender, DragEventArgs e)
		{
			HideDropIndicator();

			if (!e.DataView.Properties.TryGetValue(BaseTabBar.TabPathIdentifier, out var raw) ||
				raw is not string serialized)
				return;

			var zone = GetDropZone(e);
			if (zone is PaneDropZone.None)
				return;

			TabBarItemParameter tabArgs;
			try
			{
				tabArgs = TabBarItemParameter.Deserialize(serialized);
			}
			catch (JsonException)
			{
				return;
			}

			var draggedPath = (tabArgs.NavigationParameter as PaneNavigationArguments)?.LeftPaneNavPathParam
				?? tabArgs.NavigationParameter as string
				?? string.Empty;
			var nearSide = zone is PaneDropZone.Left or PaneDropZone.Top;
			var arrangement = zone is PaneDropZone.Left or PaneDropZone.Right
				? ShellPaneArrangement.Vertical
				: ShellPaneArrangement.Horizontal;
			var currentPath = GetPane(0)?.TabBarItemParameter?.NavigationParameter as string ?? "Home";

			OpenSecondaryPane(nearSide ? currentPath : draggedPath, arrangement);
			if (nearSide)
			{
				NavParamsLeft = new() { NavPath = string.IsNullOrEmpty(draggedPath) ? "Home" : draggedPath };
				// Override AddPane's focus on the new pane; the dropped content lives in pane 0 here.
				ActivePane = GetPane(0);
			}

			// Self-drop: leave the close-on-drop flag unset so the source tab survives the split.
			if (!ReferenceEquals(_draggedTabItem?.TabItemContent, this))
				AppDataCompat.LocalSettingsValues[BaseTabBar.TabDropHandledIdentifier] = true;
		}

		private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
		{
			WindowIsCompact = MainWindow.Instance.Bounds.Width <= Constants.UI.MultiplePaneWidthThreshold;
		}

		[DynamicWindowsRuntimeCast(typeof(UIElement))]
		private void Pane_Loaded(object sender, RoutedEventArgs e)
		{
			if (sender is UIElement element)
			{
				element.GotFocus += Pane_GotFocus;
				element.RightTapped += Pane_RightTapped;
				element.AddHandler(UIElement.PointerPressedEvent, _panePointerPressedHandler, true);
			}
		}

		private void Pane_ContentChanged(object? sender, TabBarItemParameter e)
		{
			TabBarItemParameter = new()
			{
				InitialPageType = typeof(ShellPanesPage),
				NavigationParameter = new PaneNavigationArguments()
				{
					LeftPaneNavPathParam = GetPane(0)?.TabBarItemParameter?.NavigationParameter as string ?? e?.NavigationParameter as string,
					RightPaneNavPathParam = GetPaneCount() >= 2 ? GetPane(1)?.TabBarItemParameter?.NavigationParameter as string : null,
					ThirdPaneNavPathParam = GetPaneCount() >= 3 ? GetPane(2)?.TabBarItemParameter?.NavigationParameter as string : null,
					FourthPaneNavPathParam = GetPaneCount() >= 4 ? GetPane(3)?.TabBarItemParameter?.NavigationParameter as string : null,
					ShellPaneArrangement = ShellPaneArrangement,
				}
			};
		}

		[DynamicWindowsRuntimeCast(typeof(UIElement))]
		[DynamicWindowsRuntimeCast(typeof(ButtonBase))]
		private void Pane_PointerPressed(object sender, PointerRoutedEventArgs e)
		{
			// A button cancels its press once it loses focus, so leave focus alone while one is being pressed
			if ((e.OriginalSource as DependencyObject)?.FindAscendantOrSelf<ButtonBase>() is not null)
				return;

			// Focus pane if interaction suggests intent to focus:
			// 1. Sender is not the currently active pane (user is switching panes), or the sender is the active pane,
			// but the user is refocusing the pane (e.g. user taps pane to refocus while the Omnibar flyout is open)
			// 2. AND the sender is a valid shell page not using a column-based layout
			if (((IsMultiPaneActive && sender != ActivePane) || e.Pointer.PointerDeviceType == PointerDeviceType.Touch) && sender is IShellPage shellPage && shellPage.SlimContentPage is not ColumnsLayoutPage)
				(sender as UIElement)?.Focus(FocusState.Pointer);
		}

		private void Pane_GotFocus(object sender, RoutedEventArgs e)
		{
			var focusedPane = sender as ModernShellPage;
			var isFocusedPaneActive = focusedPane is not null && ActivePane == focusedPane;

			// Clear selection in all other panes
			foreach (var pane in GetPanes())
			{
				if (pane == focusedPane || pane.SlimContentPage?.IsItemSelected != true)
					continue;

				pane.SlimContentPage.LockPreviewPaneContent = true;
				pane.SlimContentPage.ItemManipulationModel.ClearSelection();
				pane.SlimContentPage.LockPreviewPaneContent = false;
			}

			if (!isFocusedPaneActive && focusedPane is not null && ActivePane != (focusedPane as IShellPage))
				ActivePane = focusedPane;
		}

		[DynamicWindowsRuntimeCast(typeof(UIElement))]
		private void Pane_RightTapped(object sender, RoutedEventArgs e)
		{
			if (sender != ActivePane && sender is IShellPage shellPage && shellPage.SlimContentPage is not ColumnsLayoutPage)
				((UIElement)sender).Focus(FocusState.Programmatic);
		}

		private void Sizer_Loaded(object sender, RoutedEventArgs e)
		{
			if (sender is GridSplitter sizer)
			{
				sizer.ChangeCursor(
					InputSystemCursor.Create(
						ShellPaneArrangement is ShellPaneArrangement.Vertical
							? InputSystemCursorShape.SizeWestEast
							: InputSystemCursorShape.SizeNorthSouth));
			}
		}

		private void Sizer_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
		{
			if (ShellPaneArrangement is ShellPaneArrangement.Grid)
			{
				foreach (var definition in RootGrid.ColumnDefinitions.Where(x => RootGrid.ColumnDefinitions.IndexOf(x) % 2 == 0))
					definition.Width = new GridLength(1, GridUnitType.Star);
				foreach (var definition in RootGrid.RowDefinitions.Where(x => RootGrid.RowDefinitions.IndexOf(x) % 2 == 0))
					definition.Height = new GridLength(1, GridUnitType.Star);
			}
			else if (ShellPaneArrangement is ShellPaneArrangement.Vertical)
			{
				var definitions = RootGrid.ColumnDefinitions.Where(x => RootGrid.ColumnDefinitions.IndexOf(x) % 2 == 0);
				definitions?.ForEach(x => x.Width = new GridLength(1, GridUnitType.Star));
			}
			else
			{
				var definitions = RootGrid.RowDefinitions.Where(x => RootGrid.RowDefinitions.IndexOf(x) % 2 == 0);
				definitions?.ForEach(x => x.Height = new GridLength(1, GridUnitType.Star));
			}
		}

		private void Sizer_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
		{
			this.ChangeCursor(
				InputSystemCursor.Create(
					ShellPaneArrangement is ShellPaneArrangement.Vertical
						? InputSystemCursorShape.SizeWestEast
						: InputSystemCursorShape.SizeNorthSouth));
		}

		private void Sizer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
		{
			// Remove a pane when it has been dragged below the minimum size
			for (var i = 1; i < GetPaneCount(); i++)
			{
				if (GetPane(i) is ModernShellPage pane &&
					(pane.ActualWidth <= 100 || pane.ActualHeight <= 100))
				{
					RemovePane(i);
					break;
				}
			}

			this.ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.Arrow));
		}

		private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		// Disposer

		public void Dispose()
		{
			App.Logger.LogInformation($"ShellPanesPage.Dispose: PaneCount={GetPaneCount()}, ActivePane={LogPathHelper.RedactPath(ActivePane?.TabBarItemParameter?.NavigationParameter?.ToString())}");

			TabBar.TabDragStarted -= TabBar_TabDragStarted;
			TabBar.TabDragCompleted -= TabBar_TabDragCompleted;

			MainWindow.Instance.SizeChanged -= MainWindow_SizeChanged;

			// Dispose panes
			foreach (var pane in GetPanes())
			{
				pane.Loaded -= Pane_Loaded;
				pane.ContentChanged -= Pane_ContentChanged;
				pane.GotFocus -= Pane_GotFocus;
				pane.RightTapped -= Pane_RightTapped;
				pane.RemoveHandler(UIElement.PointerPressedEvent, _panePointerPressedHandler);
				pane.Dispose();
			}

			// Dispose sizers
			foreach (var sizer in GetSizers())
			{
				sizer.DoubleTapped -= Sizer_OnDoubleTapped;
				sizer.Loaded -= Sizer_Loaded;
				sizer.ManipulationCompleted -= Sizer_ManipulationCompleted;
				sizer.ManipulationStarted -= Sizer_ManipulationStarted;
			}

			_ActivePane = null;
			RootGrid.Children.Clear();
			RootGrid.RowDefinitions.Clear();
			RootGrid.ColumnDefinitions.Clear();
		}
	}
}
