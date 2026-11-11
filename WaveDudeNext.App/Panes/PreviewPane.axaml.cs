// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WaveDudeNext.App.Commands;
using WaveDudeNext.App.Controls;
using WaveDudeNext.App.Rendering;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;
using WaveDudeNext.UI.Rendering;
using Path = Avalonia.Controls.Shapes.Path;

namespace WaveDudeNext.App.Panes;

/// <summary>
/// The document in the centre: the field and the intensity picture, side by side or stacked, with a transport bar to
/// scrub, play and pause through the frames of a run at a chosen speed. The frames come from the <see cref="Run"/>'s
/// store through a <see cref="FramePresenter"/>, which colours the frame at <see cref="Position"/> with the project's
/// display settings (colour scheme, brightness, black level; changing them re-colours the frame shown). Without a run
/// the pictures are black, or the scene image. The frame count follows the project's step count and visualisation
/// interval, and so do the <see cref="InPoint"/>/<see cref="OutPoint"/> of the export range (<c>Display.ExportFrom/To</c>,
/// in steps; the range commands write them). Playback runs between the two, looping when <see cref="IsLooping"/>.
/// </summary>
/// <remarks>
/// While a run is in progress the axis spans the planned frames (<see cref="PlannedLastFrame"/>) and
/// <see cref="LastFrame"/> is the last frame that exists so far; <see cref="IsLive"/> keeps <see cref="Position"/> on
/// that newest frame until the user scrubs, steps or plays, and the Live button (or F) re-attaches. Playback runs on a
/// thread timer whose ticks are posted to the UI thread at render priority (upstream's design: a dispatcher timer was
/// starved by a stream of mouse moves): a speed of 1 advances <see cref="Fps"/> (the project's video frame rate) frames
/// per second of wall-clock time, measured with a stopwatch so that a slow tick does not slow the clock. The window
/// hands the pane <see cref="Project"/>, <see cref="Run"/>, <see cref="ScratchFrame"/>, <see cref="IsStale"/>,
/// <see cref="IsProbesStale"/>, <see cref="IsComputing"/>, <see cref="ScenePixels"/>, <see cref="DitherBits"/>, the
/// selection and <see cref="Commands"/>; it places the timeline bar in <see cref="TimelineHost"/> and the scene overlays
/// in <see cref="OverlayHostOf"/>. Keyboard: every transport control is a tab stop; the split radios are one stop that
/// lands on the checked one, Left/Right there check the other; arrows move the splitter, Enter (or a double-click)
/// resets it to the middle; Space toggles playback from anywhere in the pane except a text box, and a click on the
/// pictures focuses the pane so Space works after it. UI thread.
/// </remarks>
public partial class PreviewPane : UserControl
{
	/// <summary>Smallest accepted <see cref="Speed"/>.</summary>
	public const double MinSpeed = 0.01;

	/// <summary>Largest accepted <see cref="Speed"/>.</summary>
	public const double MaxSpeed = 100.0;

	/// <summary>Speed factors offered in the drop-down; any other value can be typed.</summary>
	public static IReadOnlyList<double> SpeedPresets { get; } = [0.25, 0.5, 1.0, 1.5, 2.0, 4.0];

	/// <summary>Smallest brightness the header's box accepts.</summary>
	public const double MinBrightness = 0.0001;

	/// <summary>Largest brightness the header's box accepts.</summary>
	public const double MaxBrightness = 100000.0;

	/// <summary>The ticker's period: 60 ticks a second asked for; the stopwatch keeps the pace whatever the system gives.</summary>
	private const int TickMilliseconds = 16;

	/// <summary>How long a frame may take before the pictures dim and the spinner shows; shorter waits pass unmarked, so a fast machine does not strobe.</summary>
	private static readonly TimeSpan BusyOverlayDelay = TimeSpan.FromMilliseconds(150);

	// ---------------------------------------------------------------- properties the window sets

	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly StyledProperty<Project?> ProjectProperty =
		AvaloniaProperty.Register<PreviewPane, Project?>(nameof(Project));

	/// <summary>Identifies the <see cref="Run"/> property.</summary>
	public static readonly StyledProperty<SimulationRun?> RunProperty =
		AvaloniaProperty.Register<PreviewPane, SimulationRun?>(nameof(Run));

	/// <summary>Identifies the <see cref="SelectedItem"/> property.</summary>
	public static readonly StyledProperty<object?> SelectedItemProperty =
		AvaloniaProperty.Register<PreviewPane, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

	/// <summary>Identifies the <see cref="SelectedItems"/> property.</summary>
	public static readonly StyledProperty<ObservableCollection<object>?> SelectedItemsProperty =
		AvaloniaProperty.Register<PreviewPane, ObservableCollection<object>?>(nameof(SelectedItems));

	/// <summary>Identifies the <see cref="IsStale"/> property.</summary>
	public static readonly StyledProperty<bool> IsStaleProperty =
		AvaloniaProperty.Register<PreviewPane, bool>(nameof(IsStale));

	/// <summary>Identifies the <see cref="IsProbesStale"/> property.</summary>
	public static readonly StyledProperty<bool> IsProbesStaleProperty =
		AvaloniaProperty.Register<PreviewPane, bool>(nameof(IsProbesStale));

	/// <summary>Identifies the <see cref="ScratchFrame"/> property.</summary>
	public static readonly StyledProperty<ScratchResult?> ScratchFrameProperty =
		AvaloniaProperty.Register<PreviewPane, ScratchResult?>(nameof(ScratchFrame));

	/// <summary>Identifies the <see cref="IsComputing"/> property.</summary>
	public static readonly StyledProperty<bool> IsComputingProperty =
		AvaloniaProperty.Register<PreviewPane, bool>(nameof(IsComputing));

	/// <summary>Identifies the <see cref="Position"/> property.</summary>
	public static readonly StyledProperty<int> PositionProperty =
		AvaloniaProperty.Register<PreviewPane, int>(nameof(Position), 0, defaultBindingMode: BindingMode.TwoWay, coerce: CoercePosition);

	/// <summary>Identifies the <see cref="IsPlaying"/> property.</summary>
	public static readonly StyledProperty<bool> IsPlayingProperty =
		AvaloniaProperty.Register<PreviewPane, bool>(nameof(IsPlaying));

	/// <summary>Identifies the <see cref="IsPlaybackHeld"/> property.</summary>
	public static readonly StyledProperty<bool> IsPlaybackHeldProperty =
		AvaloniaProperty.Register<PreviewPane, bool>(nameof(IsPlaybackHeld));

	/// <summary>Identifies the <see cref="ColorsVersion"/> property.</summary>
	public static readonly StyledProperty<int> ColorsVersionProperty =
		AvaloniaProperty.Register<PreviewPane, int>(nameof(ColorsVersion));

	/// <summary>Identifies the <see cref="IsLive"/> property.</summary>
	public static readonly StyledProperty<bool> IsLiveProperty =
		AvaloniaProperty.Register<PreviewPane, bool>(nameof(IsLive), defaultBindingMode: BindingMode.TwoWay);

	/// <summary>Identifies the <see cref="IsLooping"/> property.</summary>
	public static readonly StyledProperty<bool> IsLoopingProperty =
		AvaloniaProperty.Register<PreviewPane, bool>(nameof(IsLooping), defaultBindingMode: BindingMode.TwoWay);

	/// <summary>Identifies the <see cref="Orientation"/> property.</summary>
	public static readonly StyledProperty<Orientation> OrientationProperty =
		AvaloniaProperty.Register<PreviewPane, Orientation>(nameof(Orientation), Orientation.Horizontal, defaultBindingMode: BindingMode.TwoWay);

	/// <summary>Identifies the <see cref="DitherBits"/> property.</summary>
	public static readonly StyledProperty<int> DitherBitsProperty =
		AvaloniaProperty.Register<PreviewPane, int>(nameof(DitherBits));

	/// <summary>Identifies the <see cref="ScenePixels"/> property.</summary>
	public static readonly StyledProperty<ScenePixels?> ScenePixelsProperty =
		AvaloniaProperty.Register<PreviewPane, ScenePixels?>(nameof(ScenePixels));

	/// <summary>Identifies the <see cref="Speed"/> property.</summary>
	public static readonly StyledProperty<double> SpeedProperty =
		AvaloniaProperty.Register<PreviewPane, double>(nameof(Speed), 1.0, defaultBindingMode: BindingMode.TwoWay, coerce: CoerceSpeed);

	/// <summary>Identifies the <see cref="Commands"/> property.</summary>
	public static readonly StyledProperty<AppCommands> CommandsProperty =
		AvaloniaProperty.Register<PreviewPane, AppCommands>(nameof(Commands), new AppCommands());

	// ---------------------------------------------------------------- read-only properties the pane derives

	/// <summary>Identifies the <see cref="PlannedLastFrame"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> PlannedLastFrameProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(PlannedLastFrame), pane => pane.PlannedLastFrame);

	/// <summary>Identifies the <see cref="LastFrame"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> LastFrameProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(LastFrame), pane => pane.LastFrame);

	/// <summary>Identifies the <see cref="Available"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> AvailableProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(Available), pane => pane.Available);

	/// <summary>Identifies the <see cref="InPoint"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> InPointProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(InPoint), pane => pane.InPoint);

	/// <summary>Identifies the <see cref="OutPoint"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> OutPointProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(OutPoint), pane => pane.OutPoint);

	/// <summary>Identifies the <see cref="FrameCount"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> FrameCountProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(FrameCount), pane => pane.FrameCount);

	/// <summary>Identifies the <see cref="Fps"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> FpsProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(Fps), pane => pane.Fps);

	/// <summary>Identifies the <see cref="ShownFrame"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, ShownFrame?> ShownFrameProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, ShownFrame?>(nameof(ShownFrame), pane => pane.ShownFrame);

	/// <summary>Identifies the <see cref="CanvasWidth"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> CanvasWidthProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(CanvasWidth), pane => pane.CanvasWidth);

	/// <summary>Identifies the <see cref="CanvasHeight"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, int> CanvasHeightProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, int>(nameof(CanvasHeight), pane => pane.CanvasHeight);

	/// <summary>Identifies the <see cref="GraphScratch"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, ScratchResult?> GraphScratchProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, ScratchResult?>(nameof(GraphScratch), pane => pane.GraphScratch);

	/// <summary>Identifies the <see cref="IsBusyOverlayVisible"/> property.</summary>
	public static readonly DirectProperty<PreviewPane, bool> IsBusyOverlayVisibleProperty =
		AvaloniaProperty.RegisterDirect<PreviewPane, bool>(nameof(IsBusyOverlayVisible), pane => pane.IsBusyOverlayVisible);

	private int _plannedLastFrame;
	private int _lastFrame;
	private int _available;
	private int _inPoint;
	private int _outPoint;
	private int _frameCount = 1;
	private int _fps = Video.DefaultFps;
	private int _canvasWidth = 1;
	private int _canvasHeight = 1;
	private ScratchResult? _graphScratch;
	private bool _isBusyOverlayVisible;

	// ---------------------------------------------------------------- state

	private readonly FramePresenter _presenter;
	private readonly Timer _ticker;
	private int _tickPending;
	private readonly DispatcherTimer _busyDelay;
	private readonly Stopwatch _clock = new();
	private readonly PictureCell _fieldCell;
	private readonly PictureCell _intensityCell;
	private readonly List<(INotifyPropertyChanged Source, PropertyChangedEventHandler Handler)> _hooks = [];
	private readonly List<(MenuItem Item, Func<AppCommands, ICommand> Pick)> _menuCommands = [];
	private readonly RelayCommand _play, _pause, _stepBack, _stepForward, _setIn, _setOut, _clearRange;
	private IDisposable? _runTip, _runName, _pauseRunTip, _pauseRunName;
	private TopLevel? _topLevel;
	private WriteableBitmap? _sceneBitmap;
	private byte[]? _edges;
	private SnapshotFrameSource? _scratchSource;
	private ScratchResult? _scratchShown;
	private ShownFrame? _shownFrame;
	private double _carry;
	private bool _following;
	private bool _syncing;
	private readonly bool _ready;

	/// <summary>Creates the pane: the picture cells, the transport commands, the ticker and the busy timer.</summary>
	public PreviewPane()
	{
		InitializeComponent();
		IsTabStop = false;

		_presenter = new FramePresenter();
		// a 16-bit picture of a new size arrives with its first frame
		_presenter.BitmapsChanged += (_, _) => UpdateCanvas(_presenter.Source);
		// an Avalonia Image keeps drawing what it last saw: told after every frame written into its bitmap
		_presenter.FramePresented += (_, _) =>
		{
			_fieldCell!.Picture.InvalidateVisual();
			_intensityCell!.Picture.InvalidateVisual();
		};

		_fieldCell = BuildCell(ProbeLayer.Field, "SectionField", "PreviewFieldImage");
		_intensityCell = BuildCell(ProbeLayer.Intensity, "SectionIntensity", "PreviewIntensityImage");
		// field cell, splitter, intensity cell, as upstream declares them (PreviewPane.xaml:724-750): the Tab order runs
		// through the field picture's header, the splitter, then the intensity picture's header
		pictures.Children.Insert(0, _fieldCell.Frame);
		pictures.Children.Add(_intensityCell.Frame);
		pictures.PointerPressed += OnPicturesPointerPressed;
		AddHandler(LostFocusEvent, OnChildLostFocus);

		// the pane's own transport commands; the run's commands come from the window through Commands
		_play = new RelayCommand(() => IsPlaying = true, () => !IsPlaying && LastFrame > 0);
		_pause = new RelayCommand(() => IsPlaying = false, () => IsPlaying);
		_stepBack = new RelayCommand(StepBack, CanTransport);
		_stepForward = new RelayCommand(StepForward, CanTransport);
		_setIn = new RelayCommand(SetInPoint, CanEditRange);
		_setOut = new RelayCommand(SetOutPoint, CanEditRange);
		_clearRange = new RelayCommand(ClearRange, CanClearRange);
		SetRangeStartCommand = new RelayCommand<int>(SetInPointAt, _ => CanEditRange());
		SetRangeEndCommand = new RelayCommand<int>(SetOutPointAt, _ => CanEditRange());
		playButton.Command = _play;
		pauseButton.Command = _pause;
		stepBackButton.Command = _stepBack;
		stepForwardButton.Command = _stepForward;
		setInButton.Command = _setIn;
		setOutButton.Command = _setOut;
		clearRangeButton.Command = _clearRange;
		loopToggle.IsCheckedChanged += (_, _) => IsLooping = loopToggle.IsChecked == true;
		liveToggle.IsCheckedChanged += (_, _) =>
		{
			if (!_syncing && liveToggle.IsChecked == true != IsLive)
			{
				IsLive = liveToggle.IsChecked == true;
			}
		};
		showFieldToggle.IsCheckedChanged += (_, _) => SetPreview(FieldSettings, showFieldToggle);
		showIntensityToggle.IsCheckedChanged += (_, _) => SetPreview(IntensitySettings, showIntensityToggle);
		sideBySideRadio.IsCheckedChanged += (_, _) => OnSplitRadioChanged();
		stackedRadio.IsCheckedChanged += (_, _) => OnSplitRadioChanged();
		splitGroup.KeyDown += OnSplitGroupKeyDown;
		pictureSplitter.DoubleTapped += (_, _) => ResetSplit();
		pictureSplitter.KeyDown += OnSplitterKeyDown;

		speedBox.ItemsSource = SpeedPresets.Select(preset => preset.ToString(CultureInfo.CurrentCulture)).ToList();
		speedBox.Text = Speed.ToString(CultureInfo.CurrentCulture);
		speedBox.SelectionChanged += (_, _) => CommitSpeed();
		speedBox.LostFocus += (_, _) => CommitSpeed();
		// Space inside the speed box is neither a digit nor the play/pause toggle: swallowed before the box or the pane see
		// it, the key and the space it types (Avalonia delivers the character as its own event, however the key went;
		// upstream's handled PreviewKeyDown stopped both, since a WPF text box takes Space on the key)
		speedBox.AddHandler(KeyDownEvent, OnSpeedBoxKeyDown, RoutingStrategies.Tunnel);
		speedBox.AddHandler(TextInputEvent, OnSpeedBoxTextInput, RoutingStrategies.Tunnel);

		// a thread timer that posts the tick to the UI thread at render priority, not a DispatcherTimer (upstream's rule:
		// a Win32 timer message was starved by a stream of mouse moves and no frame came for seconds; a posted operation
		// is taken before input). One tick in flight at a time, so a slow UI thread does not pile them up
		_ticker = new Timer(_ =>
		{
			if (Interlocked.Exchange(ref _tickPending, 1) == 0)
			{
				Dispatcher.UIThread.Post(() =>
				{
					Volatile.Write(ref _tickPending, 0);
					OnTick();
				}, DispatcherPriority.Render);
			}
		});
		_busyDelay = new DispatcherTimer(DispatcherPriority.Normal) { Interval = BusyOverlayDelay };
		_busyDelay.Tick += (_, _) =>
		{
			_busyDelay.Stop();
			IsBusyOverlayVisible = IsComputing;
		};

		_ready = true;
		WireCommands();
		UpdateFrameCount();
		UpdateRange();
		UpdateRunButton();
		UpdatePictureLayout();
		UpdateEdgeLine();
		UpdateSource();
		UpdateReadout();
		PlaceTimelineAndOverlays();
	}

	/// <summary>The WPF pane's shading of the computed frames once the project was edited: grey instead of the accent.</summary>
	private static readonly IBrush StaleAvailableBrush = new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80));
	private TimelineBar? _timeline;

	/// <summary>
	/// The timeline bar under the pictures and one scene overlay over each picture, bound to this pane's properties as the
	/// WPF pane declares them in its markup (2026-11-09; the port's window had placed them from outside).
	/// </summary>
	private void PlaceTimelineAndOverlays()
	{
		var timeline = new TimelineBar { Margin = new Thickness(4, 2) };
		timeline.Bind(TimelineBar.MaximumProperty, OwnBinding(nameof(PlannedLastFrame)));
		timeline.Bind(TimelineBar.AvailableProperty, OwnBinding(nameof(Available)));
		timeline.Bind(TimelineBar.ValueProperty, OwnBinding(nameof(Position), BindingMode.TwoWay));
		timeline.Bind(TimelineBar.RangeStartProperty, OwnBinding(nameof(InPoint)));
		timeline.Bind(TimelineBar.RangeEndProperty, OwnBinding(nameof(OutPoint)));
		timeline.SetRangeStartCommand = SetRangeStartCommand;
		timeline.SetRangeEndCommand = SetRangeEndCommand;
		// the bar's own Background and Foreground, not the pane's inherited ones of the same name (2026-11-10: the bar had
		// come out dark with a black playhead for a day)
		timeline[!TimelineBar.BackgroundProperty] = new DynamicResourceExtension("ScrollBarBackground");
		timeline[!TimelineBar.ForegroundProperty] = new DynamicResourceExtension("WindowForeground");
		timeline[!TimelineBar.TickBrushProperty] = new DynamicResourceExtension("TimelineTick");
		timeline[!TimelineBar.OutlineBrushProperty] = new DynamicResourceExtension("ControlBorder");
		timeline[!TimelineBar.LabelBrushProperty] = new DynamicResourceExtension("GrayText");
		timeline[!TimelineBar.AccentBrushProperty] = new DynamicResourceExtension("Accent");
		timeline[!ToolTip.TipProperty] = new DynamicResourceExtension("TipPreviewTimeline");
		timeline[!AutomationProperties.NameProperty] = new DynamicResourceExtension("TipPreviewTimeline");
		AutomationProperties.SetAutomationId(timeline, "PreviewTimeline");
		_timeline = timeline;
		ShadeAvailable(IsStale);
		TimelineHost.Content = timeline;
		foreach (ProbeLayer layer in new[] { ProbeLayer.Field, ProbeLayer.Intensity })
		{
			var overlay = new SceneOverlay { Layer = layer };
			overlay.Bind(SceneOverlay.CommandsProperty, OwnBinding(nameof(Commands)));
			overlay.Bind(SceneOverlay.ProjectProperty, OwnBinding(nameof(Project)));
			overlay.Bind(SceneOverlay.CanvasWidthProperty, OwnBinding(nameof(CanvasWidth)));
			overlay.Bind(SceneOverlay.CanvasHeightProperty, OwnBinding(nameof(CanvasHeight)));
			overlay.Bind(SceneOverlay.SelectedItemProperty, OwnBinding(nameof(SelectedItem), BindingMode.TwoWay));
			overlay.Bind(SceneOverlay.SelectedItemsProperty, OwnBinding(nameof(SelectedItems)));
			overlay.Bind(SceneOverlay.HandlesInPreviewProperty, OwnBinding("Project.Display.HandlesInPreview"));
			overlay.Bind(SceneOverlay.LabelsInPreviewProperty, OwnBinding("Project.Display.LabelsInPreview"));
			overlay[!SceneOverlay.LineBrushProperty] = new DynamicResourceExtension("PictureHandle");
			overlay[!SceneOverlay.LabelBrushProperty] = new DynamicResourceExtension("PictureHandle");
			overlay[!SceneOverlay.AccentBrushProperty] = new DynamicResourceExtension("PictureSelection");
			OverlayHostOf(layer).Children.Add(overlay);
		}
	}

	/// <summary>A binding to one of this pane's properties, one way unless asked otherwise.</summary>
	private Binding OwnBinding(string path, BindingMode mode = BindingMode.OneWay) => new(path) { Source = this, Mode = mode };

	/// <summary>The computed frames' shade on the bar: the theme's fill, or grey once the project was edited since the run.</summary>
	private void ShadeAvailable(bool stale)
	{
		if (_timeline == null)
		{
			return;
		}
		if (stale)
		{
			_timeline.AvailableBrush = StaleAvailableBrush;
		}
		else
		{
			_timeline[!TimelineBar.AvailableBrushProperty] = new DynamicResourceExtension("DropPreviewFill");
		}
	}

	// ---------------------------------------------------------------- the picture cells

	/// <summary>One picture with its header: the controls the pane reaches after building them.</summary>
	private sealed class PictureCell
	{
		public required ProbeLayer Layer { get; init; }
		public required PictureFrame Frame { get; init; }
		public required HeaderStrip Header { get; init; }
		public required NumberBox Brightness { get; init; }
		public required Button Auto { get; init; }
		public required Border Edge { get; init; }
		public required CanvasImage Picture { get; init; }
		public required Image Outline { get; init; }
		public required Panel OverlayHost { get; init; }
		public required ProbeGraphOverlay Graphs { get; init; }
		public required Panel Busy { get; init; }
		public required Path Spinner { get; init; }
	}

	/// <summary>
	/// Builds one picture cell as upstream's cell template: a <see cref="PictureFrame"/> holding the header strip (the
	/// title, the brightness caption and, as the tools, the brightness box with its Auto button) and the 1 px edge-line
	/// border around a grid that stacks the picture, the scene outline, the host for the scene overlay, the probe graph
	/// overlay and the busy layer. The brightness box is upstream's logarithmic NumberBox (0.0001 to 100000, PreviewPane.xaml:586):
	/// Up/Down by two percent, PageUp/PageDown by ten steps, the spin buttons, the mouse wheel, a drag on the text to scrub,
	/// bound to the layer's Brightness as the Display view's box is, with Enter to commit and Escape to revert (EditBehavior).
	/// </summary>
	private PictureCell BuildCell(ProbeLayer layer, string titleKey, string idPrefix)
	{
		var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
		title.Bind(TextBlock.TextProperty, this.GetResourceObservable(titleKey));
		title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("WindowForeground"));
		var caption = new TextBlock { Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
		caption.Bind(TextBlock.TextProperty, this.GetResourceObservable("Label.DisplayLayer.Brightness"));
		caption.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("WindowForeground"));
		var brightness = new NumberBox { Width = 64, Minimum = MinBrightness, Maximum = MaxBrightness, Logarithmic = true, VerticalAlignment = VerticalAlignment.Center };
		// the layer's Brightness through the pane's project, written back when the box loses the focus or on Enter, as
		// upstream's Text binding (WPF's LostFocus default) and the Display view's box do; a step writes at once
		brightness.Bind(TextBox.TextProperty, new Binding($"{nameof(Project)}.{nameof(Model.Project.Display)}.{(layer == ProbeLayer.Field ? nameof(Display.Field) : nameof(Display.Intensity))}.{nameof(DisplayLayer.Brightness)}")
		{
			Source = this,
			Mode = BindingMode.TwoWay,
			UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
		});
		brightness.Bind(ToolTip.TipProperty, this.GetResourceObservable("Hint.DisplayLayer.Brightness"));
		brightness.Bind(AutomationProperties.NameProperty, this.GetResourceObservable("Label.DisplayLayer.Brightness"));
		AutomationProperties.SetAutomationId(brightness, idPrefix + "Brightness");
		var auto = new Button { Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 0), MinHeight = 22, CommandParameter = layer };
		auto.Bind(ContentProperty, this.GetResourceObservable("ButtonAutoBrightness"));
		auto.Bind(ToolTip.TipProperty, this.GetResourceObservable("TipAutoBrightness"));
		AutomationProperties.SetAutomationId(auto, idPrefix + "AutoBrightness");
		var tools = new StackPanel { Orientation = Orientation.Horizontal, Children = { brightness, auto } };
		// above the picture, no margin over it; turned on its side it keeps 10 px (the user's request)
		var header = new HeaderStrip { Margin = new Thickness(0, 0, 0, 4), Children = { title, caption, tools } };

		var picture = new CanvasImage();
		AutomationProperties.SetAutomationId(picture, idPrefix);
		picture.SizeChanged += (_, _) => SetPictureSize(layer, picture);
		var outline = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
		var overlayHost = new Panel();
		var graphs = new ProbeGraphOverlay
		{
			Layer = layer,
			BoxTheme = this.TryFindResource("ProbeBoxThumb", out object? boxTheme) ? boxTheme as ControlTheme : null,
			CornerTheme = this.TryFindResource("ProbeBoxCornerThumb", out object? cornerTheme) ? cornerTheme as ControlTheme : null,
		};
		graphs.Bind(ProbeGraphOverlay.AccentBrushProperty, this.GetResourceObservable("PictureSelection"));
		graphs.PropertyChanged += (_, e) =>
		{
			if (e.Property == ProbeGraphOverlay.SelectedItemProperty && !_syncing)
			{
				_syncing = true;
				SelectedItem = graphs.SelectedItem;
				_syncing = false;
			}
		};
		var spinner = new Path
		{
			Width = 36,
			Height = 36,
			Stroke = Brushes.White,
			StrokeThickness = 3,
			StrokeLineCap = PenLineCap.Round,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			RenderTransform = new RotateTransform(),
			RenderTransformOrigin = RelativePoint.Center,
			Data = SharedGeometry("SpinnerGeometry"),
		};
		// a frame for this position is being computed (longer than a moment): the stale picture dims and a spinner turns
		var busy = new Grid { IsHitTestVisible = false, IsVisible = false, Children = { new Rectangle { Fill = Brushes.Black, Opacity = 0.45 }, spinner } };
		// black under the picture: what shows before any frame, in the canvas's size so the overlays and the aspect ratio are right
		var stack = new Grid { Background = Brushes.Black, Children = { picture, outline, overlayHost, graphs, busy } };
		// the edge line: 1 px around the picture, red for a reflective wall, blue for a damping rim outside the canvas
		var edge = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = stack };
		var frame = new PictureFrame { Margin = new Thickness(4), Children = { header, edge }, ContextMenu = BuildContextMenu(layer) };
		frame.PropertyChanged += (_, e) =>
		{
			if (e.Property == PictureFrame.IsLabelOnSideProperty)
			{
				header.Margin = frame.IsLabelOnSide ? new Thickness(0, 10, 0, 4) : new Thickness(0, 0, 0, 4);
			}
		};
		return new PictureCell
		{
			Layer = layer,
			Frame = frame,
			Header = header,
			Brightness = brightness,
			Auto = auto,
			Edge = edge,
			Picture = picture,
			Outline = outline,
			OverlayHost = overlayHost,
			Graphs = graphs,
			Busy = busy,
			Spinner = spinner,
		};
	}

	/// <summary>The picture's context menu: the brightness first, then a still of this picture, then the exports in the File menu's order.</summary>
	private ContextMenu BuildContextMenu(ProbeLayer layer)
	{
		MenuItem brightness = MenuEntry("MenuBrightness", "ContextBrightness", drawing: "Brightness");
		brightness.Click += (_, _) => EditBrightness(layer);
		MenuItem auto = MenuEntry("MenuAutoBrightness", "ContextAutoBrightness", commands => commands.AutoBrightness, layer, drawing: "AutoBrightness");
		MenuItem frame = MenuEntry("MenuExportThisFrame", "ContextExportFrame", commands => commands.ExportFrame, layer, layer == ProbeLayer.Field ? "Ctrl+Shift+F" : "Ctrl+Shift+G", "Scene");
		MenuItem field = MenuEntry("MenuFileExportField", "ContextExportField", commands => commands.ExportField, null, "Ctrl+Shift+E", "Export");
		MenuItem intensity = MenuEntry("MenuFileExportIntensity", "ContextExportIntensity", commands => commands.ExportIntensity, null, "Ctrl+Shift+I", "Export");
		MenuItem both = MenuEntry("MenuFileExportBoth", "ContextExportBoth", commands => commands.ExportBoth, null, "Ctrl+Shift+B", "Export");
		return new ContextMenu { Items = { brightness, auto, new Separator(), frame, new Separator(), field, intensity, both } };
	}

	/// <summary>An entry of the picture's menu: the text by key, the command picked from the window's when they arrive, the icon one of the shared drawings as in the WPF head's menu.</summary>
	private MenuItem MenuEntry(string headerKey, string automationId, Func<AppCommands, ICommand>? pick = null, object? parameter = null, string? gesture = null, string? drawing = null)
	{
		var item = new MenuItem { CommandParameter = parameter };
		MenuIcon.SetDrawing(item, drawing);
		item.Bind(HeaderedSelectingItemsControl.HeaderProperty, this.GetResourceObservable(headerKey));
		AutomationProperties.SetAutomationId(item, automationId);
		if (gesture != null)
		{
			item.InputGesture = KeyGesture.Parse(gesture);
		}
		if (pick != null)
		{
			_menuCommands.Add((item, pick));
		}
		return item;
	}

	private PictureCell CellOf(ProbeLayer layer) => layer == ProbeLayer.Field ? _fieldCell : _intensityCell;

	private static FrameLayer FrameLayerOf(ProbeLayer layer) => layer == ProbeLayer.Field ? FrameLayer.Field : FrameLayer.Intensity;

	/// <summary>
	/// A geometry of the shared dictionary (Themes/Icons.axaml, which the application merges), asked of the application
	/// itself: the pane builds its pictures and sets its Run button in its constructor, before it has joined a window,
	/// where a lookup from the pane would end at the pane. Null when the application has none under that key.
	/// </summary>
	private static Geometry? SharedGeometry(string key) =>
		Application.Current?.TryGetResource(key, null, out object? value) == true ? value as Geometry : null;

	private PictureCell CellOf(FrameLayer layer) => layer == FrameLayer.Field ? _fieldCell : _intensityCell;

	/// <summary>Where the window puts the timeline bar: above the transport bar, full width.</summary>
	public ContentControl TimelineHost => timelineHost;

	/// <summary>Where the window puts the scene overlay (the source, part and probe handles) of a picture: over the frame, under the probe graphs.</summary>
	public Panel OverlayHostOf(ProbeLayer layer) => CellOf(layer).OverlayHost;

	// ---------------------------------------------------------------- the properties

	/// <summary>The project whose run is shown; null shows the placeholders with a single frame.</summary>
	public Project? Project
	{
		get => GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>The run whose frames are shown (in progress or finished); null shows the placeholders.</summary>
	public SimulationRun? Run
	{
		get => GetValue(RunProperty);
		set => SetValue(RunProperty, value);
	}

	/// <summary>The source or probe selected in the project: highlighted on the pictures, set by grabbing or focusing a marker. Bound to the main window.</summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		set => SetValue(SelectedItemProperty, value);
	}

	/// <summary>The shared selection of sources, parts and probes (the main window's), handed to the overlays, which highlight, extend and drag it.</summary>
	public ObservableCollection<object>? SelectedItems
	{
		get => GetValue(SelectedItemsProperty);
		set => SetValue(SelectedItemsProperty, value);
	}

	/// <summary>Whether the project was edited since the run started, so the stored frames no longer show it; the timeline shades the available frames grey.</summary>
	public bool IsStale
	{
		get => GetValue(IsStaleProperty);
		set => SetValue(IsStaleProperty, value);
	}

	/// <summary>Whether a probe was edited since the run started: its frames hold, its recordings do not. Bound to the main window.</summary>
	public bool IsProbesStale
	{
		get => GetValue(IsProbesStaleProperty);
		set => SetValue(IsProbesStaleProperty, value);
	}

	/// <summary>
	/// A frame computed for the project as it is now (after an edit, without a run); shown instead of the stored frame
	/// while its frame index is <see cref="Position"/> and the store has nothing newer. Null for none.
	/// </summary>
	public ScratchResult? ScratchFrame
	{
		get => GetValue(ScratchFrameProperty);
		set => SetValue(ScratchFrameProperty, value);
	}

	/// <summary>Whether a frame for <see cref="Position"/> is being computed (set by the main window); after <see cref="BusyOverlayDelay"/> the pictures dim and a spinner turns until it arrives.</summary>
	public bool IsComputing
	{
		get => GetValue(IsComputingProperty);
		set => SetValue(IsComputingProperty, value);
	}

	/// <summary>Frame shown, 0 to <see cref="LastFrame"/>. Moved by the timeline and by playback.</summary>
	public int Position
	{
		get => GetValue(PositionProperty);
		set => SetValue(PositionProperty, value);
	}

	/// <summary>Whether playback is running. Setting it starts or stops the frame clock.</summary>
	public bool IsPlaying
	{
		get => GetValue(IsPlayingProperty);
		set => SetValue(IsPlayingProperty, value);
	}

	/// <summary>
	/// Whether the frame clock waits though playback is on (bound to the application's <c>PreviewState.IsPlaybackHeld</c>):
	/// the run Play asked for is still being decided, the memory warning open, as in the WPF pane since 2026-11-09.
	/// </summary>
	public bool IsPlaybackHeld
	{
		get => GetValue(IsPlaybackHeldProperty);
		set => SetValue(IsPlaybackHeldProperty, value);
	}

	/// <summary>Counts the application's colour changes (<c>PreviewState.ColorsVersion</c>): the palettes are rebuilt from the project's schemes when it changes.</summary>
	public int ColorsVersion
	{
		get => GetValue(ColorsVersionProperty);
		set => SetValue(ColorsVersionProperty, value);
	}

	/// <summary>Whether <see cref="Position"/> follows the newest frame of a run in progress. Only while a run is active; scrubbing, stepping, playing or the run's end turns it off.</summary>
	public bool IsLive
	{
		get => GetValue(IsLiveProperty);
		set => SetValue(IsLiveProperty, value);
	}

	/// <summary>Whether playback wraps from the out point back to the in point instead of stopping.</summary>
	public bool IsLooping
	{
		get => GetValue(IsLoopingProperty);
		set => SetValue(IsLoopingProperty, value);
	}

	/// <summary>How the two pictures are split: <see cref="Orientation.Horizontal"/> side by side, <see cref="Orientation.Vertical"/> field above intensity.</summary>
	public Orientation Orientation
	{
		get => GetValue(OrientationProperty);
		set => SetValue(OrientationProperty, value);
	}

	/// <summary>
	/// The dithering of the 16-bit pictures (View > Preview dithering): 0 for none, else the bits per channel the colours
	/// carry into the Bayer matrix (10, 12, 14, 16). Preview only; the 8-bit pictures are not dithered.
	/// </summary>
	public int DitherBits
	{
		get => GetValue(DitherBitsProperty);
		set => SetValue(DitherBitsProperty, value);
	}

	/// <summary>
	/// The decoded scene image of an image scene, null for a blank canvas. It is the picture shown where no frame exists,
	/// gives the canvas its size before any run, and is the source of the glowing-edges overlays.
	/// </summary>
	public ScenePixels? ScenePixels
	{
		get => GetValue(ScenePixelsProperty);
		set => SetValue(ScenePixelsProperty, value);
	}

	/// <summary>Playback speed factor, <see cref="MinSpeed"/> to <see cref="MaxSpeed"/>; 1 is <see cref="Fps"/> frames per second.</summary>
	public double Speed
	{
		get => GetValue(SpeedProperty);
		set => SetValue(SpeedProperty, value);
	}

	/// <summary>The application's commands: the run's (Run, Stop, TogglePause, EnsureRun), the exports, Auto brightness, Remove and Select all.</summary>
	public AppCommands Commands
	{
		get => GetValue(CommandsProperty);
		set => SetValue(CommandsProperty, value);
	}

	/// <summary>Index of the last frame of the whole run (the run's own count while one exists, else the project's); the timeline's axis.</summary>
	public int PlannedLastFrame
	{
		get => _plannedLastFrame;
		private set => SetAndRaise(PlannedLastFrameProperty, ref _plannedLastFrame, value);
	}

	/// <summary>Index of the last frame that can be shown: the newest frame of the run, or <see cref="PlannedLastFrame"/> without a run.</summary>
	public int LastFrame
	{
		get => _lastFrame;
		private set => SetAndRaise(LastFrameProperty, ref _lastFrame, value);
	}

	/// <summary>Frames of the run computed so far; 0 without a run.</summary>
	public int Available
	{
		get => _available;
		private set => SetAndRaise(AvailableProperty, ref _available, value);
	}

	/// <summary>First frame of the export/loop range; 0 while no in point is set. Follows the model's <c>Display.ExportFrom</c>.</summary>
	public int InPoint
	{
		get => _inPoint;
		private set => SetAndRaise(InPointProperty, ref _inPoint, value);
	}

	/// <summary>Last frame of the export/loop range; <see cref="PlannedLastFrame"/> while no out point is set. Follows the model's <c>Display.ExportTo</c>.</summary>
	public int OutPoint
	{
		get => _outPoint;
		private set => SetAndRaise(OutPointProperty, ref _outPoint, value);
	}

	/// <summary>Number of frames the project's settings would produce, at least 1: ceil(steps / visualise every).</summary>
	public int FrameCount
	{
		get => _frameCount;
		private set => SetAndRaise(FrameCountProperty, ref _frameCount, value);
	}

	/// <summary>Frames per second at a <see cref="Speed"/> of 1: the project's video frame rate, so speed 1 plays as the export would.</summary>
	public int Fps
	{
		get => _fps;
		private set => SetAndRaise(FpsProperty, ref _fps, Math.Max(1, value));
	}

	/// <summary>Width in pixels of the canvas the pictures show: the run's while one exists, else the project's blank canvas size.</summary>
	public int CanvasWidth
	{
		get => _canvasWidth;
		private set => SetAndRaise(CanvasWidthProperty, ref _canvasWidth, value);
	}

	/// <summary>Height in pixels of the canvas the pictures show.</summary>
	public int CanvasHeight
	{
		get => _canvasHeight;
		private set => SetAndRaise(CanvasHeightProperty, ref _canvasHeight, value);
	}

	/// <summary>
	/// The scratch frame the probe graphs draw from, or null while they draw from the run: the scratch frame the pictures
	/// show, or, with only the probes stale (<see cref="IsProbesStale"/>: the pictures keep the stored frame, a probe
	/// never changes it), the scratch frame computed for the position.
	/// </summary>
	public ScratchResult? GraphScratch
	{
		get => _graphScratch;
		private set
		{
			if (SetAndRaise(GraphScratchProperty, ref _graphScratch, value))
			{
				_fieldCell.Graphs.Scratch = value;
				_intensityCell.Graphs.Scratch = value;
			}
		}
	}

	/// <summary>Whether the dim and the spinner are shown over the pictures: <see cref="IsComputing"/> has lasted longer than <see cref="BusyOverlayDelay"/>.</summary>
	public bool IsBusyOverlayVisible
	{
		get => _isBusyOverlayVisible;
		private set
		{
			if (SetAndRaise(IsBusyOverlayVisibleProperty, ref _isBusyOverlayVisible, value))
			{
				foreach (PictureCell cell in new[] { _fieldCell, _intensityCell })
				{
					cell.Busy.IsVisible = value;
					cell.Spinner.Classes.Set("spinning", value);
				}
			}
		}
	}

	/// <summary>
	/// The frames the pictures show and the index of the one shown (the scratch frame, index 0, or the run's frame at the
	/// position), or null when nothing is shown; written by the pane and bound OneWayToSource by the window for the
	/// application's brightness fit (<see cref="PreviewState.ShownFrame"/>, as the WPF pane binds it; 2026-11-11), which
	/// fits a shown 16-bit frame at once instead of computing it again.
	/// </summary>
	public ShownFrame? ShownFrame
	{
		get => _shownFrame;
		private set => SetAndRaise(ShownFrameProperty, ref _shownFrame, value);
	}

	/// <summary>Sets the in point of the export range at the frame given as the parameter (the timeline's bracket drag).</summary>
	public ICommand SetRangeStartCommand { get; }

	/// <summary>Sets the out point of the export range at the frame given as the parameter.</summary>
	public ICommand SetRangeEndCommand { get; }

	private DisplayLayer? FieldSettings => Project?.Display.Field;

	private DisplayLayer? IntensitySettings => Project?.Display.Intensity;

	private DisplayLayer? SettingsOf(FrameLayer layer) => layer == FrameLayer.Field ? FieldSettings : IntensitySettings;

	// ---------------------------------------------------------------- property changes

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (!_ready)
		{
			return;
		}
		if (change.Property == ProjectProperty)
		{
			OnProjectChanged(change.GetNewValue<Project?>());
		}
		else if (change.Property == RunProperty)
		{
			OnRunChanged(change.GetOldValue<SimulationRun?>(), change.GetNewValue<SimulationRun?>());
		}
		else if (change.Property == PositionProperty)
		{
			OnPositionChanged();
		}
		else if (change.Property == IsPlayingProperty)
		{
			OnIsPlayingChanged(change.GetNewValue<bool>());
		}
		else if (change.Property == IsPlaybackHeldProperty)
		{
			UpdateClock();
		}
		else if (change.Property == IsStaleProperty)
		{
			ShadeAvailable(change.GetNewValue<bool>());
		}
		else if (change.Property == ColorsVersionProperty)
		{
			RefreshColors();
		}
		else if (change.Property == IsLiveProperty)
		{
			OnIsLiveChanged(change.GetNewValue<bool>());
		}
		else if (change.Property == IsLoopingProperty)
		{
			loopToggle.IsChecked = change.GetNewValue<bool>();
		}
		else if (change.Property == OrientationProperty)
		{
			OnOrientationChanged();
		}
		else if (change.Property == DitherBitsProperty)
		{
			_presenter.DitherBits = change.GetNewValue<int>();
		}
		else if (change.Property == ScenePixelsProperty)
		{
			OnScenePixelsChanged();
		}
		else if (change.Property == IsStaleProperty || change.Property == IsProbesStaleProperty || change.Property == ScratchFrameProperty)
		{
			UpdateSource();
		}
		else if (change.Property == IsComputingProperty)
		{
			OnComputingChanged();
		}
		else if (change.Property == SpeedProperty)
		{
			speedBox.Text = Speed.ToString(CultureInfo.CurrentCulture);
		}
		else if (change.Property == CommandsProperty)
		{
			// the set's members as they are now, and again whenever the window replaces one (AppCommands announces it)
			if (change.OldValue is AppCommands old)
			{
				old.PropertyChanged -= OnCommandReplaced;
			}
			if (change.NewValue is AppCommands commands)
			{
				commands.PropertyChanged += OnCommandReplaced;
			}
			WireCommands();
		}
		else if (change.Property == SelectedItemProperty)
		{
			OnSelectedItemChanged();
		}
	}

	private void OnCommandReplaced(object? sender, PropertyChangedEventArgs e) => WireCommands();

	/// <summary>Hands the set's members to the controls that run them: the picture menus, the Auto buttons, the probe graph boxes, Pause and the Run button.</summary>
	private void WireCommands()
	{
		AppCommands commands = Commands;
		foreach ((MenuItem item, Func<AppCommands, ICommand> pick) in _menuCommands)
		{
			item.Command = pick(commands);
		}
		_fieldCell.Auto.Command = commands.AutoBrightness;
		_intensityCell.Auto.Command = commands.AutoBrightness;
		_fieldCell.Graphs.Commands = commands;
		_intensityCell.Graphs.Commands = commands;
		pauseRunButton.Command = commands.TogglePause;
		UpdateRunButton();
	}

	private void OnSelectedItemChanged()
	{
		if (_syncing)
		{
			return;
		}
		_syncing = true;
		_fieldCell.Graphs.SelectedItem = SelectedItem;
		_intensityCell.Graphs.SelectedItem = SelectedItem;
		_syncing = false;
	}

	// ---------------------------------------------------------------- the project and its settings

	/// <summary>Another project (new, opened, imported or closed): re-wires the model's notifications; playback stops and the timeline goes back to frame 0 (upstream's rule).</summary>
	private void OnProjectChanged(Project? project)
	{
		Unhook();
		if (project != null)
		{
			Hook(project);
		}
		_fieldCell.Graphs.Project = project;
		_intensityCell.Graphs.Project = project;
		IsPlaying = false;
		IsLive = false;
		Position = 0;
		RefreshFromProject();
	}

	/// <summary>Everything that follows the project's settings, re-derived: the frame count and range, the clock, the colours, the outlines, the layout, the edge line, the canvas.</summary>
	private void RefreshFromProject()
	{
		UpdateFrameCount();
		Fps = Project?.Display.Video.Fps ?? Video.DefaultFps;
		ApplyLayer(FrameLayer.Field);
		ApplyLayer(FrameLayer.Intensity);
		UpdateOverlay(FrameLayer.Field);
		UpdateOverlay(FrameLayer.Intensity);
		UpdateShowToggles();
		UpdatePictureLayout();
		UpdateEdgeLine();
		UpdateSource();
		UpdateReadout();
		RequeryTransport();
	}

	private void Hook(Project project)
	{
		Add(project, OnProjectMemberChanged);
		Add(project.Scene, OnSceneChanged);
		Add(project.Simulation, OnSimulationChanged);
		Add(project.Display, OnDisplayChanged);
		Add(project.Display.Field, OnFieldSettingsChanged);
		Add(project.Display.Intensity, OnIntensitySettingsChanged);
		Add(project.Display.Video, OnVideoChanged);
	}

	private void Add(INotifyPropertyChanged source, PropertyChangedEventHandler handler)
	{
		source.PropertyChanged += handler;
		_hooks.Add((source, handler));
	}

	private void Unhook()
	{
		foreach ((INotifyPropertyChanged source, PropertyChangedEventHandler handler) in _hooks)
		{
			source.PropertyChanged -= handler;
		}
		_hooks.Clear();
	}

	/// <summary>The model replaced one of its parts wholesale (deserialisation does): everything is re-wired.</summary>
	private void Rehook()
	{
		Unhook();
		if (Project is { } project)
		{
			Hook(project);
		}
		RefreshFromProject();
	}

	private void OnProjectMemberChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(Model.Project.Scene) or nameof(Model.Project.Simulation) or nameof(Model.Project.Display))
		{
			Rehook();
		}
	}

	private void OnSceneChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(Scene.Width):
			case nameof(Scene.Height):
				UpdateSource();
				break;
			case nameof(Scene.Edges):
			case nameof(Scene.DampingEdge):
				UpdateEdgeLine();
				break;
		}
	}

	private void OnSimulationChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(Simulation.Steps) or nameof(Simulation.VisualizeEvery))
		{
			UpdateFrameCount();
			UpdateReadout();
		}
	}

	private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(Display.ExportFrom):
			case nameof(Display.ExportTo):
				UpdateRange();
				RequeryTransport();
				break;
			case nameof(Display.Field):
			case nameof(Display.Intensity):
			case nameof(Display.Video):
				Rehook();
				break;
		}
	}

	private void OnVideoChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(Video.Fps))
		{
			Fps = Project?.Display.Video.Fps ?? Video.DefaultFps;
		}
	}

	private void OnFieldSettingsChanged(object? sender, PropertyChangedEventArgs e) => OnLayerSettingChanged(FrameLayer.Field, e.PropertyName);

	private void OnIntensitySettingsChanged(object? sender, PropertyChangedEventArgs e) => OnLayerSettingChanged(FrameLayer.Intensity, e.PropertyName);

	/// <summary>An outline setting rebuilds that layer's overlay; anything else re-applies the palette.</summary>
	private void OnLayerSettingChanged(FrameLayer layer, string? property)
	{
		if (property is nameof(DisplayLayer.Overlay) or nameof(DisplayLayer.OverlayGain))
		{
			UpdateOverlay(layer);
		}
		else if (property != nameof(DisplayLayer.OverlayInExport))
		{
			ApplyLayer(layer);
		}
		if (property == nameof(DisplayLayer.Preview))
		{
			UpdateShowToggles();
			UpdatePictureLayout();
		}
	}

	/// <summary>Hands a layer's palette, brightness and visibility to the presenter, which redraws the frame shown.</summary>
	private void ApplyLayer(FrameLayer layer)
	{
		DisplayLayer? settings = SettingsOf(layer);
		ColorLut lut = settings == null ? ColorLut.Grey : ColorSchemeCatalog.Instance.LutFor(Project, settings.ColorScheme, settings.BlackLevel);
		_presenter.SetLayer(layer, lut, (float)(settings?.Brightness ?? DisplayLayer.DefaultBrightness), settings?.Preview ?? true);
	}

	/// <summary>Rebuilds both palettes and redraws: a project colour scheme changed, or the project was replaced.</summary>
	public void RefreshColors()
	{
		ApplyLayer(FrameLayer.Field);
		ApplyLayer(FrameLayer.Intensity);
	}

	/// <summary>Rebuilds one layer's outline from its settings: null when switched off, at gain 0, or without a scene image.</summary>
	private void UpdateOverlay(FrameLayer layer)
	{
		DisplayLayer? settings = SettingsOf(layer);
		CellOf(layer).Outline.Source = _edges != null && ScenePixels is { } pixels && settings is { Overlay: true }
			? SceneBitmaps.Overlay(_edges, pixels.Width, pixels.Height, settings.OverlayGain)
			: null;
	}

	/// <summary>A new scene image (or none): rebuilds the picture, the edge picture and both overlays, and re-derives the canvas.</summary>
	private void OnScenePixelsChanged()
	{
		ScenePixels? pixels = ScenePixels;
		_sceneBitmap = pixels == null ? null : SceneBitmaps.Image(pixels);
		_edges = pixels == null ? null : GlowingEdges.Compute(pixels.Bgra, pixels.Width, pixels.Height, pixels.StrideBytes);
		UpdateOverlay(FrameLayer.Field);
		UpdateOverlay(FrameLayer.Intensity);
		UpdateSource();
	}

	/// <summary>The 1 px line around a picture, in the colour of what a wave meets at the canvas edge: blue for a damping rim outside the canvas (it wins, the wave meets it first), red for a reflective wall, nothing for open edges.</summary>
	private void UpdateEdgeLine()
	{
		Scene? scene = Project?.Scene;
		IBrush brush = scene is { HasRim: true } ? Brushes.Blue : scene is { Edges: Model.EdgeMode.Reflective } ? Brushes.Red : Brushes.Transparent;
		_fieldCell.Edge.BorderBrush = brush;
		_intensityCell.Edge.BorderBrush = brush;
	}

	private void UpdateShowToggles()
	{
		_syncing = true;
		showFieldToggle.IsEnabled = FieldSettings != null;
		showIntensityToggle.IsEnabled = IntensitySettings != null;
		showFieldToggle.IsChecked = FieldSettings?.Preview ?? true;
		showIntensityToggle.IsChecked = IntensitySettings?.Preview ?? true;
		_syncing = false;
	}

	private void SetPreview(DisplayLayer? settings, ToggleButton toggle)
	{
		if (!_syncing && settings != null && toggle.IsChecked is bool shown && settings.Preview != shown)
		{
			settings.Preview = shown;
		}
	}

	// ---------------------------------------------------------------- the brightness box

	/// <summary>The context menu's "Brightness": the keyboard focus goes to the brightness box in that picture's header.</summary>
	public void EditBrightness(ProbeLayer layer)
	{
		NumberBox box = CellOf(layer).Brightness;
		box.Focus();
		box.SelectAll();
	}

	// ---------------------------------------------------------------- the run and its frames

	private void OnRunChanged(SimulationRun? old, SimulationRun? run)
	{
		if (old != null)
		{
			old.PropertyChanged -= OnRunPropertyChanged;
		}
		if (run != null)
		{
			run.PropertyChanged += OnRunPropertyChanged;
		}
		_fieldCell.Graphs.Run = run;
		_intensityCell.Graphs.Run = run;
		if (run is { IsAutomatic: true, IsFinished: false })
		{
			// a re-run after an edit: the position stays where the user is (the axis stays the planned run, so nothing
			// coerces it), the scratch frame stays on screen until the store has that frame, and the run is not followed
			// whatever the Live button said
			UpdateFrames();
			IsLive = false;
		}
		else if (run == null)
		{
			// the run was discarded (replaced, or the project closed): keep the position for whatever comes next
			UpdateFrames();
			IsLive = false;
		}
		else
		{
			// a run the user started: from the beginning, following it; playback (if any) stops
			IsPlaying = false;
			UpdateFrames();
			Position = 0;
			// a run handed over before Start is still "Created": follow everything that has not ended
			IsLive = !run.IsFinished;
		}
		UpdateSource();
		UpdateRunButton();
		RequeryTransport();
	}

	private void OnRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(SimulationRun.FrameCount):
				UpdateFrames();
				if (IsLive && !_presenter.IsBusy)
				{
					// follow the newest frame, but only when the previous picture is done: the next heartbeat (30 a
					// second) follows again, so the position trails the run by one picture at most
					_following = true;
					Position = NewestFrame;
					_following = false;
				}
				UpdateSource();
				break;
			case nameof(SimulationRun.State):
				UpdateFrames();
				if (IsLive && Run?.IsFinished == true)
				{
					// the end: follow to the last frame whatever the presenter is doing (no heartbeat comes after the end)
					_following = true;
					Position = NewestFrame;
					_following = false;
				}
				UpdateSource();
				if (Run?.IsFinished == true)
				{
					IsLive = false; // nothing left to follow
				}
				UpdateRunButton();
				RequeryTransport();
				break;
		}
	}

	/// <summary>
	/// Decides what the pictures show. A picture changes only when a frame for <see cref="Position"/> itself is there:
	/// the scratch frame computed for it (preferred while the run is stale, missing, or not that far yet), else the run's
	/// stored frame. When neither exists (a run in progress that has not reached the position, after the previous run's
	/// frames were discarded) the last picture simply stays; the frames are never replayed from the start and never
	/// replaced by black or by some other frame. Also re-derives the canvas size.
	/// </summary>
	private void UpdateSource()
	{
		SimulationRun? run = Run;
		ScratchResult? scratch = ScratchFrame;
		bool useScratch = scratch != null && scratch.Frame == Position && (run == null || IsStale || run.FrameCount <= Position);
		if (useScratch)
		{
			// a new result is a new picture, whichever of the runner's two snapshots it came in (the WPF pane's rule of
			// 2026-11-09, ported 2026-11-11): the runner finishes the frame in flight, so a scrub leaves outcomes for
			// positions already left, which take their turn of the two snapshots, and the frame the scrub ends on comes,
			// as often as not, in the snapshot on screen; comparing the snapshots had then made no new source, and
			// nothing was drawn
			if (_scratchSource == null || !ReferenceEquals(_scratchShown, scratch))
			{
				_scratchSource = new SnapshotFrameSource(scratch!.Snapshot);
				_scratchShown = scratch;
			}
			_presenter.Source = _scratchSource;
			GraphScratch = scratch;
			UpdateCanvas(_scratchSource);
			ShownFrame = new ShownFrame(_scratchSource, 0);
			_presenter.Show(0);
		}
		else if (run != null && run.FrameCount > Position)
		{
			// the run's frames in timeline numbering (a reduced preview stands a stored frame in for the ones left out)
			_presenter.Source = run.Frames;
			// the stored frame is right even with the probes stale; the graphs then take the scratch recording for this position once it is there
			GraphScratch = IsProbesStale && scratch != null && scratch.Frame == Position ? scratch : null;
			UpdateCanvas(run.Frames);
			ShownFrame = new ShownFrame(run.Frames, Position);
			_presenter.Show(Position);
		}
		else if (run == null && scratch == null && _presenter.Source != null)
		{
			// nothing at all any more (project closed or replaced: the window reset the scratch frame too). With a scratch
			// frame for another position pending, the last picture stays: clearing here made every scrub without a run
			// flash the empty picture before the new frame arrived
			_presenter.Source = null;
			GraphScratch = null;
			ShownFrame = null;
			UpdateCanvas(null);
		}
		else
		{
			// nothing for this position yet: keep what is shown
			UpdateCanvas(_presenter.Source);
		}
	}

	/// <summary>The canvas the pictures show (the frame's, the run's, the scene image's or the project's blank size) and the bitmaps in the pictures.</summary>
	private void UpdateCanvas(IFrameSource? source)
	{
		// the scene image's size wins over the project's numbers, which only apply to a blank canvas; a frame source knows
		// the canvas it was shrunk from (the handles and boxes live in canvas pixels, whatever the picture's own size is)
		int width = Math.Max(1, source?.CanvasWidth ?? Run?.Spec.Width ?? ScenePixels?.Width ?? Project?.Scene.Width ?? Scene.DefaultWidth);
		int height = Math.Max(1, source?.CanvasHeight ?? Run?.Spec.Height ?? ScenePixels?.Height ?? Project?.Scene.Height ?? Scene.DefaultHeight);
		CanvasWidth = width;
		CanvasHeight = height;
		foreach (PictureCell cell in new[] { _fieldCell, _intensityCell })
		{
			cell.Picture.CanvasWidth = width;
			cell.Picture.CanvasHeight = height;
			cell.Graphs.CanvasWidth = width;
			cell.Graphs.CanvasHeight = height;
		}
		// without a frame, an image scene shows the scene image itself, a blank canvas the black behind the picture
		WriteableBitmap? empty = _sceneBitmap is { } scene && scene.PixelSize.Width == width && scene.PixelSize.Height == height ? scene : null;
		_fieldCell.Picture.Source = _presenter.FieldBitmap ?? empty;
		_intensityCell.Picture.Source = _presenter.IntensityBitmap ?? empty;
	}

	/// <summary>The frames the project's settings would produce: ceil(steps / visualise every), at least 1; the axis is always the project's.</summary>
	private void UpdateFrameCount()
	{
		Simulation? simulation = Project?.Simulation;
		int every = Math.Max(1, simulation?.VisualizeEvery ?? 1);
		FrameCount = simulation == null ? 1 : Math.Max(1, (simulation.Steps + every - 1) / every);
		UpdateFrames();
	}

	/// <summary>
	/// Re-derives the axis, the reachable last frame and the available count from the run (or the project without one).
	/// For an automatic re-run the reachable end is always the planned run, filling or cancelled by the next edit, so the
	/// user's position is never pulled back; only a run the user started (and follows, or stopped) bounds the position by
	/// the frames that exist.
	/// </summary>
	private void UpdateFrames()
	{
		SimulationRun? run = Run;
		PlannedLastFrame = (run?.Spec.FrameCount ?? FrameCount) - 1;
		Available = run?.FrameCount ?? 0;
		LastFrame = run == null || run.IsAutomatic ? PlannedLastFrame : Math.Max(0, run.FrameCount - 1);
		CoerceValue(PositionProperty);
		UpdateRange();
		UpdateReadout();
		RequeryTransport();
	}

	/// <summary>The in and out points in frames from the model's steps; an ExportTo of 0 means the end of the run.</summary>
	private void UpdateRange()
	{
		Display? display = Project?.Display;
		int every = Project?.Simulation.VisualizeEvery ?? 1;
		InPoint = display == null ? 0 : StepToFrame(display.ExportFrom, every, PlannedLastFrame, end: false);
		OutPoint = display == null ? PlannedLastFrame : StepToFrame(display.ExportTo, every, PlannedLastFrame, end: true);
	}

	/// <summary>
	/// The index of the frame that shows a simulation step, clamped to 0..last frame. With <paramref name="end"/> a step
	/// of 0 means the end of the run and gives the last frame, which is how <c>Display.ExportTo</c> encodes "no out point".
	/// </summary>
	internal static int StepToFrame(int step, int every, int lastFrame, bool end)
	{
		if (step <= 0)
		{
			return end ? lastFrame : 0;
		}
		return Math.Clamp(every > 0 ? step / every : 0, 0, lastFrame);
	}

	/// <summary>The newest frame that exists: the run's last written frame, or the project's last frame without a run.</summary>
	private int NewestFrame => Run != null ? Math.Max(0, Run.FrameCount - 1) : PlannedLastFrame;

	private static int CoercePosition(AvaloniaObject target, int value) => Math.Clamp(value, 0, ((PreviewPane)target).LastFrame);

	private void OnPositionChanged()
	{
		if (!_following)
		{
			IsLive = false;
		}
		_fieldCell.Graphs.Frame = Position;
		_intensityCell.Graphs.Frame = Position;
		UpdateSource();
		UpdateReadout();
		RequeryTransport();
	}

	private void OnIsLiveChanged(bool live)
	{
		_syncing = true;
		liveToggle.IsChecked = live;
		_syncing = false;
		if (live)
		{
			IsPlaying = false;
			_following = true;
			Position = NewestFrame;
			_following = false;
		}
	}

	private void UpdateReadout()
	{
		CultureInfo culture = CultureInfo.CurrentCulture;
		frameReadout.Text = string.Create(culture, $"{Position} / {LastFrame}");
		int every = Project?.Simulation.VisualizeEvery ?? 1;
		string label = this.TryFindResource("LabelPreviewStep", out object? value) && value is string text ? text : "step";
		stepReadout.Text = string.Create(culture, $"{label} {Position * every}");
	}

	/// <summary>A frame started computing: the overlay follows after the delay; the frame arrived: it goes at once.</summary>
	private void OnComputingChanged()
	{
		if (IsComputing)
		{
			_busyDelay.Stop();
			_busyDelay.Start();
		}
		else
		{
			_busyDelay.Stop();
			IsBusyOverlayVisible = false;
		}
	}

	// ---------------------------------------------------------------- picture sizes

	/// <summary>
	/// A picture's size on screen changed (the window, the split, one picture or two): the presenter renders a 16-bit
	/// frame at that size in device pixels, the picture's rounded layout size at the window's scale, so its pixels land
	/// 1:1 on the screen's.
	/// </summary>
	private void SetPictureSize(ProbeLayer layer, CanvasImage picture)
	{
		double scale = _topLevel?.RenderScaling ?? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
		_presenter.SetTargetSize(layer == ProbeLayer.Field ? FrameLayer.Field : FrameLayer.Intensity, (int)Math.Round(picture.Bounds.Width * scale), (int)Math.Round(picture.Bounds.Height * scale));
	}

	/// <summary>Both pictures' sizes told again, after a scale change (a window landing on a monitor of another scale changes the device pixels while the layout stays).</summary>
	private void RefreshPictureSizes()
	{
		SetPictureSize(ProbeLayer.Field, _fieldCell.Picture);
		SetPictureSize(ProbeLayer.Intensity, _intensityCell.Picture);
	}

	/// <inheritdoc/>
	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		_topLevel = TopLevel.GetTopLevel(this);
		if (_topLevel != null)
		{
			_topLevel.ScalingChanged += OnScalingChanged;
		}
		RefreshPictureSizes();
	}

	/// <inheritdoc/>
	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		if (_topLevel != null)
		{
			_topLevel.ScalingChanged -= OnScalingChanged;
			_topLevel = null;
		}
		IsPlaying = false;
		_busyDelay.Stop();
		base.OnDetachedFromVisualTree(e);
	}

	private void OnScalingChanged(object? sender, EventArgs e) => RefreshPictureSizes();

	// ---------------------------------------------------------------- playback

	private static double CoerceSpeed(AvaloniaObject target, double speed) => double.IsNaN(speed) ? 1.0 : Math.Clamp(speed, MinSpeed, MaxSpeed);

	/// <summary>The last frame playback can reach: the out point, or the newest frame while the run has not got there yet.</summary>
	private int PlayEnd => Math.Min(OutPoint, NewestFrame);

	private void OnIsPlayingChanged(bool playing)
	{
		// read before anything re-queries the transport: Avalonia takes the focus from a button the moment the re-query
		// disables it (OnChildLostFocus passes it on to the pane, not to the counterpart)
		bool playFocused = playButton.IsFocused, pauseFocused = pauseButton.IsFocused;
		if (playing)
		{
			IsLive = false;
			if (Position >= OutPoint)
			{
				Position = InPoint; // play the range again from its start
			}
			// playing means showing the simulation: the application starts a run when there is none or it is stale. While
			// the run is being decided (the memory warning open) playback is held (IsPlaybackHeld), which keeps the clock
			// off under the question, whether the command returns with the answer or before it; a refused run turns
			// playback off again, and the hold's release starts the clock otherwise
			Commands.EnsureRun.Execute(null);
			if (!IsPlaying)
			{
				return;
			}
		}
		UpdateClock();
		RequeryTransport();
		MoveFocusToCounterpart(playing, playFocused, pauseFocused);
	}

	/// <summary>
	/// When the Play or Pause button had keyboard focus as its command executed (<paramref name="playFocused"/>,
	/// <paramref name="pauseFocused"/>, read before the re-query disabled it), moves focus to the other button, since a
	/// disabled element cannot keep focus. Deferred behind the command re-query so the target is enabled.
	/// </summary>
	private void MoveFocusToCounterpart(bool playing, bool playFocused, bool pauseFocused)
	{
		if (playing && playFocused)
		{
			Dispatcher.UIThread.Post(() => pauseButton.Focus(), DispatcherPriority.Background);
		}
		else if (!playing && pauseFocused)
		{
			Dispatcher.UIThread.Post(() => playButton.Focus(), DispatcherPriority.Background);
		}
	}

	private void OnTick()
	{
		if (!IsPlaying)
		{
			return; // a tick posted just before Pause
		}
		TimeSpan elapsed = _clock.Elapsed;
		_clock.Restart();
		if (IsClockHeld)
		{
			return; // the time that passed while held is dropped, not banked for a jump when the hold ends
		}
		Tick(elapsed);
	}

	/// <summary>
	/// Holds the playback clock until the returned handle is disposed: the ticker leaves the playhead alone and drops the
	/// time that passes, so playback goes on from the release as though it had started then. Holds nest. The window
	/// holds it while the run Play asked for waits on the memory question: upstream asked in a modal window before its
	/// clock started (its fix of 2026-11-03, the clock had ticked through the stale frames behind the question), and
	/// here the question is awaited, so Play's command returns while it is open. The tests hold it while they wait for
	/// a run's frames and call <see cref="Tick"/> themselves, so a CPU run slower than playback does not let the ticker
	/// reach the run's newest frame before their interval starts.
	/// </summary>
	internal IDisposable HoldClock()
	{
		_clockHolds++;
		return new ClockHold(this);
	}

	/// <summary>Whether a <see cref="HoldClock"/> handle is outstanding.</summary>
	internal bool IsClockHeld => _clockHolds > 0;

	/// <summary>The frame clock runs while playback is on and not held (<see cref="IsPlaybackHeld"/>): started from zero, or stopped, when that changes.</summary>
	private void UpdateClock()
	{
		bool runs = IsPlaying && !IsPlaybackHeld;
		if (runs == _clock.IsRunning)
		{
			return;
		}
		if (runs)
		{
			_carry = 0.0;
			_clock.Restart();
			_ticker.Change(0, TickMilliseconds);
		}
		else
		{
			_ticker.Change(Timeout.Infinite, Timeout.Infinite);
			_clock.Stop();
		}
	}

	private int _clockHolds;

	/// <summary>One <see cref="HoldClock"/> handle; disposing it twice releases once.</summary>
	private sealed class ClockHold(PreviewPane pane) : IDisposable
	{
		private PreviewPane? _pane = pane;

		public void Dispose()
		{
			if (_pane != null)
			{
				_pane._clockHolds--;
				_pane = null;
			}
		}
	}

	/// <summary>
	/// Advances <see cref="Position"/> by the frames that fell due in <paramref name="elapsed"/> of wall-clock time (<see cref="Fps"/> ·
	/// <see cref="Speed"/> a second). At the out point playback stops, or wraps to the in point when <see cref="IsLooping"/>.
	/// Fractions carry over to the next tick, so slow speeds still move. The ticker calls it with the stopwatch's reading;
	/// the tests call it with a chosen interval.
	/// </summary>
	internal void Tick(TimeSpan elapsed)
	{
		if (!IsPlaying)
		{
			return;
		}
		_carry += elapsed.TotalSeconds * Fps * Speed;
		int frames = (int)Math.Floor(_carry);
		if (frames == 0 || _presenter.IsBusy)
		{
			// nothing due, or the previous picture is still being rendered: the clock keeps counting and the next tick moves
			// by all the frames that fell due meanwhile (moving the position sixty times a second while eleven pictures a
			// second could be shown redrew the overlays and the bar for nothing)
			return;
		}
		_carry -= frames;
		int next = Position + frames;
		int end = PlayEnd;
		if (next < end)
		{
			Position = next;
		}
		else if (Run is { IsActive: true } && end < OutPoint)
		{
			// the run has not got this far yet: hold at its newest frame and wait, without accumulating a jump
			_following = true;
			Position = Math.Max(Position, end);
			_following = false;
			_carry = 0.0;
		}
		else if (IsLooping && end > InPoint)
		{
			Position = InPoint + (next - end) % (end - InPoint);
		}
		else
		{
			Position = end;
			IsPlaying = false;
		}
	}

	/// <summary>Play when paused, pause when playing (Space, F7).</summary>
	public void TogglePlayPause()
	{
		if (LastFrame > 0 || IsPlaying)
		{
			IsPlaying = !IsPlaying;
		}
	}

	/// <summary>Shows the previous frame (comma).</summary>
	public void StepBack() => Position -= 1;

	/// <summary>Shows the next frame (period).</summary>
	public void StepForward() => Position += 1;

	/// <summary>Jumps to the in point (the first frame while no range is set) (Home).</summary>
	public void GoToStart() => Position = InPoint;

	/// <summary>Jumps to the out point (the last reachable frame while no range is set) (End).</summary>
	public void GoToEnd() => Position = PlayEnd;

	/// <summary>Sets the in point of the export range at the frame shown (I).</summary>
	public void SetInPoint() => SetInPointAt(Position);

	/// <summary>Sets the out point of the export range at the frame shown (O).</summary>
	public void SetOutPoint() => SetOutPointAt(Position);

	/// <summary>
	/// Sets the in point, in steps, on the model. An in point past the out point clears the out point (0 = end of the
	/// run), so the range stays ordered.
	/// </summary>
	public void SetInPointAt(int frame)
	{
		if (Project is not { } project || PlannedLastFrame <= 0)
		{
			return;
		}
		Display display = project.Display;
		int step = Math.Clamp(frame, 0, PlannedLastFrame) * project.Simulation.VisualizeEvery;
		if (display.ExportTo != 0 && display.ExportTo <= step)
		{
			display.ExportTo = 0;
		}
		display.ExportFrom = step;
	}

	/// <summary>
	/// Sets the out point on the model; the last frame means "to the end" and is stored as 0. An out point before the in
	/// point clears the in point.
	/// </summary>
	public void SetOutPointAt(int frame)
	{
		if (Project is not { } project || PlannedLastFrame <= 0)
		{
			return;
		}
		Display display = project.Display;
		frame = Math.Clamp(frame, 0, PlannedLastFrame);
		int step = frame >= PlannedLastFrame ? 0 : frame * project.Simulation.VisualizeEvery;
		if (step != 0 && display.ExportFrom >= step)
		{
			display.ExportFrom = 0;
		}
		display.ExportTo = step;
	}

	/// <summary>Clears the export range back to the whole run (X).</summary>
	public void ClearRange()
	{
		if (Project is { } project)
		{
			project.Display.ExportFrom = 0;
			project.Display.ExportTo = 0;
		}
	}

	/// <summary>Toggles looping between the in and out points (L).</summary>
	public void ToggleLoop() => IsLooping = !IsLooping;

	/// <summary>Toggles following the newest frame of a run in progress (F); only while a run is active.</summary>
	public void ToggleLive()
	{
		if (Run?.IsActive == true)
		{
			IsLive = !IsLive;
		}
	}

	private bool CanTransport() => Project != null;

	private bool CanEditRange() => Project != null && PlannedLastFrame > 0;

	private bool CanClearRange() => Project is { Display: { } display } && (display.ExportFrom != 0 || display.ExportTo != 0);

	/// <summary>Tells the transport controls to ask their commands again, and sets the states no command carries.</summary>
	private void RequeryTransport()
	{
		_play.Requery();
		_pause.Requery();
		_stepBack.Requery();
		_stepForward.Requery();
		_setIn.Requery();
		_setOut.Requery();
		_clearRange.Requery();
		((RelayCommand<int>)SetRangeStartCommand).Requery();
		((RelayCommand<int>)SetRangeEndCommand).Requery();
		liveToggle.IsEnabled = Run?.IsActive == true;
	}

	/// <summary>The Run button is Stop while a run is in progress: glyph, command, tooltip and name follow Run.IsActive; the pause button's tooltip follows a paused run.</summary>
	private void UpdateRunButton()
	{
		bool active = Run?.IsActive == true;
		runButton.Content = SharedGeometry(active ? "StopGeometry" : "RunGeometry");
		runButton.Command = active ? Commands.Stop : Commands.Run;
		Rebind(ref _runTip, runButton, ToolTip.TipProperty, active ? "TipPreviewStop" : "TipPreviewRun");
		Rebind(ref _runName, runButton, AutomationProperties.NameProperty, active ? "TipPreviewStop" : "TipPreviewRun");
		bool paused = Run?.State == RunState.Paused;
		Rebind(ref _pauseRunTip, pauseRunButton, ToolTip.TipProperty, paused ? "TipPreviewResumeRun" : "TipPreviewPauseRun");
		Rebind(ref _pauseRunName, pauseRunButton, AutomationProperties.NameProperty, paused ? "TipPreviewResumeRun" : "TipPreviewPauseRun");
	}

	/// <summary>Binds a property to a string table key (so a language switch applies), replacing the previous binding.</summary>
	private void Rebind(ref IDisposable? slot, AvaloniaObject target, AvaloniaProperty property, string key)
	{
		slot?.Dispose();
		slot = target.Bind(property, this.GetResourceObservable(key));
	}

	// ---------------------------------------------------------------- keys and the pictures

	/// <summary>
	/// The pane's shortcuts: Space toggles playback, comma and period step, I, O and X set and clear the range, L loops,
	/// F follows the run, Home and End go to the range's ends, Ctrl+A selects all. Text boxes keep their keys. The letters
	/// are swallowed even when their command cannot run right now, so they never reach a menu mnemonic.
	/// </summary>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.Handled || e.Source is TextBox or ComboBox)
		{
			return;
		}
		if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A)
		{
			Commands.SelectAll.Execute(null);
			e.Handled = true;
			return;
		}
		if (e.KeyModifiers != KeyModifiers.None)
		{
			return;
		}
		switch (e.Key)
		{
			case Key.Space:
				TogglePlayPause();
				break;
			case Key.OemComma:
				if (CanTransport())
				{
					StepBack();
				}
				break;
			case Key.OemPeriod:
				if (CanTransport())
				{
					StepForward();
				}
				break;
			case Key.I:
				SetInPoint();
				break;
			case Key.O:
				SetOutPoint();
				break;
			case Key.X:
				if (CanClearRange())
				{
					ClearRange();
				}
				break;
			case Key.L:
				ToggleLoop();
				break;
			case Key.F:
				ToggleLive();
				break;
			case Key.Home:
				if (CanTransport())
				{
					GoToStart();
				}
				break;
			case Key.End:
				if (CanTransport())
				{
					GoToEnd();
				}
				break;
			default:
				return;
		}
		e.Handled = true;
	}

	/// <summary>Space inside the speed box is neither a digit nor the play/pause toggle: swallowed.</summary>
	private void OnSpeedBoxKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Space)
		{
			e.Handled = true;
		}
		else if (e.Key == Key.Enter)
		{
			CommitSpeed();
		}
	}

	/// <summary>The space a Space key types into the speed box: swallowed with the key (<see cref="OnSpeedBoxKeyDown"/>).</summary>
	private void OnSpeedBoxTextInput(object? sender, TextInputEventArgs e)
	{
		if (e.Text == " ")
		{
			e.Handled = true;
		}
	}

	/// <summary>The typed or picked speed into <see cref="Speed"/> (clamped, NaN is 1), shown back in the box.</summary>
	private void CommitSpeed()
	{
		if (double.TryParse(speedBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double speed))
		{
			Speed = speed;
		}
		speedBox.Text = Speed.ToString(CultureInfo.CurrentCulture);
	}

	/// <summary>
	/// A control of the pane lost the keyboard focus because it was disabled (Play once a run of the user's starts, the
	/// Run button while its command turns from Stop back to Run): the focus goes where WPF's keyboard device puts it
	/// (KeyboardDevice.ReevaluateFocus, on which upstream's pane relies, PreviewPane.xaml:29 Focusable with its key
	/// bindings at :389-399): back to the control once it is enabled again, else to its nearest focusable ancestor, at
	/// the latest the pane, whose keys are the transport's. Avalonia clears the focus instead, and the next comma, Home or
	/// Space reached no pane at all. Deferred, so the command that disabled the control has finished its swap; left alone
	/// when the focus went somewhere meanwhile.
	/// </summary>
	private void OnChildLostFocus(object? sender, RoutedEventArgs e)
	{
		if (e.Source is not InputElement lost || ReferenceEquals(lost, this) || lost.IsEffectivelyEnabled)
		{
			return;
		}
		Dispatcher.UIThread.Post(() =>
		{
			if (TopLevel.GetTopLevel(this)?.FocusManager is not { } focus || focus.GetFocusedElement() != null)
			{
				return;
			}
			for (Visual? visual = lost; visual != null; visual = visual.GetVisualParent())
			{
				if (visual is InputElement candidate && candidate.Focusable && candidate.IsEffectivelyEnabled && candidate.IsEffectivelyVisible)
				{
					candidate.Focus();
					return;
				}
				if (ReferenceEquals(visual, this))
				{
					return;
				}
			}
		}, DispatcherPriority.Normal);
	}

	/// <summary>
	/// A click on the pictures gives the pane keyboard focus so that Space toggles playback, and a left click on empty
	/// space (the handles and graph boxes are thumbs, which handle their own press) deselects the current item, unless
	/// Shift or Ctrl is held: then a marquee that follows adds to the selection.
	/// </summary>
	private void OnPicturesPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(pictures).Properties.IsLeftButtonPressed && (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control)) == 0)
		{
			SelectedItem = null;
		}
		Focus();
	}

	// ---------------------------------------------------------------- the split

	private void OnOrientationChanged()
	{
		_syncing = true;
		bool horizontal = Orientation == Orientation.Horizontal;
		sideBySideRadio.IsChecked = horizontal;
		stackedRadio.IsChecked = !horizontal;
		// only the checked radio is a tab stop, so Tab lands on the current choice
		sideBySideRadio.IsTabStop = horizontal;
		stackedRadio.IsTabStop = !horizontal;
		_syncing = false;
		UpdatePictureLayout();
	}

	private void OnSplitRadioChanged()
	{
		if (!_syncing)
		{
			Orientation = stackedRadio.IsChecked == true ? Orientation.Vertical : Orientation.Horizontal;
		}
	}

	/// <summary>
	/// The arrow keys inside the split group move to the neighbouring radio and check it, as a Win32 radio group does.
	/// Only the checked radio is a tab stop, so Tab lands on the current choice.
	/// </summary>
	private void OnSplitGroupKeyDown(object? sender, KeyEventArgs e)
	{
		int step = e.Key switch { Key.Right or Key.Down => 1, Key.Left or Key.Up => -1, _ => 0 };
		if (step == 0 || e.Source is not RadioButton current)
		{
			return;
		}
		var radios = splitGroup.Children.OfType<RadioButton>().ToList();
		RadioButton next = radios[(radios.IndexOf(current) + step + radios.Count) % radios.Count];
		next.IsChecked = true;
		next.Focus();
		e.Handled = true;
	}

	/// <summary>
	/// A hidden picture gives its room to the other one: with one picture off, the other spans the whole grid and the
	/// splitter is hidden; with both on, the cells take the orientation's placement and the user's split survives, since
	/// the column and row sizes are never touched.
	/// </summary>
	private void UpdatePictureLayout()
	{
		bool field = FieldSettings?.Preview ?? true;
		bool intensity = IntensitySettings?.Preview ?? true;
		bool single = field != intensity;
		bool horizontal = Orientation == Orientation.Horizontal;
		_fieldCell.Frame.IsVisible = field;
		_intensityCell.Frame.IsVisible = intensity;
		pictureSplitter.IsVisible = field && intensity;
		Place(_fieldCell.Frame, single, horizontal, first: true);
		Place(_intensityCell.Frame, single, horizontal, first: false);
		if (horizontal)
		{
			Grid.SetColumn(pictureSplitter, 1);
			Grid.SetRow(pictureSplitter, 0);
			Grid.SetColumnSpan(pictureSplitter, 1);
			Grid.SetRowSpan(pictureSplitter, 3);
			pictureSplitter.Width = 4;
			pictureSplitter.Height = double.NaN;
			pictureSplitter.HorizontalAlignment = HorizontalAlignment.Center;
			pictureSplitter.VerticalAlignment = VerticalAlignment.Stretch;
			pictureSplitter.ResizeDirection = GridResizeDirection.Columns;
		}
		else
		{
			Grid.SetColumn(pictureSplitter, 0);
			Grid.SetRow(pictureSplitter, 1);
			Grid.SetColumnSpan(pictureSplitter, 3);
			Grid.SetRowSpan(pictureSplitter, 1);
			pictureSplitter.Width = double.NaN;
			pictureSplitter.Height = 4;
			pictureSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
			pictureSplitter.VerticalAlignment = VerticalAlignment.Center;
			pictureSplitter.ResizeDirection = GridResizeDirection.Rows;
		}
	}

	/// <summary>Side by side the cells sit in columns 0 and 2 spanning the rows; stacked, in rows 0 and 2 spanning the columns; alone, over the whole grid.</summary>
	private static void Place(Control cell, bool single, bool horizontal, bool first)
	{
		if (single)
		{
			Grid.SetColumn(cell, 0);
			Grid.SetRow(cell, 0);
			Grid.SetColumnSpan(cell, 3);
			Grid.SetRowSpan(cell, 3);
		}
		else if (horizontal)
		{
			Grid.SetColumn(cell, first ? 0 : 2);
			Grid.SetRow(cell, 0);
			Grid.SetColumnSpan(cell, 1);
			Grid.SetRowSpan(cell, 3);
		}
		else
		{
			Grid.SetColumn(cell, 0);
			Grid.SetRow(cell, first ? 0 : 2);
			Grid.SetColumnSpan(cell, 3);
			Grid.SetRowSpan(cell, 1);
		}
	}

	/// <summary>Puts the splitter back in the middle: both pictures get an equal star share of the split dimension.</summary>
	public void ResetSplit()
	{
		var half = new GridLength(1, GridUnitType.Star);
		if (Orientation == Orientation.Horizontal)
		{
			pictures.ColumnDefinitions[0].Width = half;
			pictures.ColumnDefinitions[2].Width = half;
		}
		else
		{
			pictures.RowDefinitions[0].Height = half;
			pictures.RowDefinitions[2].Height = half;
		}
	}

	/// <summary>Enter on the focused splitter is the keyboard's double-click; the arrow keys move it (built in).</summary>
	private void OnSplitterKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			ResetSplit();
			e.Handled = true;
		}
	}

	/// <summary>The grid the pictures and the splitter sit in, for the tests.</summary>
	internal Grid Pictures => pictures;

	/// <summary>The cell of a layer (its root), for the tests and the window.</summary>
	internal Control CellRootOf(ProbeLayer layer) => CellOf(layer).Frame;

	/// <summary>The probe graph overlay of a layer, for the tests.</summary>
	internal ProbeGraphOverlay GraphsOf(ProbeLayer layer) => CellOf(layer).Graphs;

	/// <summary>The brightness box of a layer, for the tests.</summary>
	internal NumberBox BrightnessBoxOf(ProbeLayer layer) => CellOf(layer).Brightness;

	/// <summary>The frame presenter behind the pictures, for the tests.</summary>
	internal FramePresenter PicturePresenter => _presenter;

	/// <summary>Every visual the pane holds, for the tests: a control by its automation id.</summary>
	internal Control? FindByAutomationId(string id) =>
		this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id);
}
