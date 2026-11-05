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
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

// Contributors: Vinicius Miguel


using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SkiaSharp;

namespace WaveDudeNext.CrossPlatform;

/// <summary>The embedded manual, its contents and figures. UI thread only.</summary>
internal sealed class ManualWindow : Window
{
	private static readonly Regex InlinePattern = new(@"(\*\*.+?\*\*|`[^`]+`|\[[^\]]+\]\([^)]+\))", RegexOptions.Compiled);
	private readonly Dictionary<string, Control> _headings = new(StringComparer.OrdinalIgnoreCase);
	private readonly TreeView _contents;
	private readonly ScrollViewer _viewer;

	/// <summary>Builds an owned, non-modal manual window.</summary>
	public ManualWindow()
	{
		Title = Resource("ManualTitle"); Width = 1200; Height = 820; MinWidth = 480; MinHeight = 300;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		AutomationProperties.SetAutomationId(this, "ManualWindow");
		var document = new StackPanel { Margin = new Thickness(24), Spacing = 5, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
		_viewer = new ScrollViewer { Content = document, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Focusable = true };
		AutomationProperties.SetAutomationId(_viewer, "ManualText");
		_contents = new TreeView(); AutomationProperties.SetAutomationId(_contents, "ManualContents"); AutomationProperties.SetName(_contents, Resource("ManualContents"));
		BuildDocument(document);
		_contents.SelectionChanged += (_, _) => { if (_contents.SelectedItem is TreeViewItem { Tag: Control heading }) heading.BringIntoView(); };
		_contents.KeyDown += (_, e) => { if (e.Key == Key.Enter && _contents.SelectedItem is TreeViewItem { Tag: Control heading }) { heading.BringIntoView(); _viewer.Focus(); e.Handled = true; } };
		var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("260,4,*") };
		var splitter = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch, Focusable = true, ResizeDirection = GridResizeDirection.Columns };
		AutomationProperties.SetAutomationId(splitter, "ManualSplitter"); AutomationProperties.SetName(splitter, Resource("TipManualSplitter")); ToolTip.SetTip(splitter, Resource("TipManualSplitter"));
		Grid.SetColumn(_contents, 0); Grid.SetColumn(splitter, 1); Grid.SetColumn(_viewer, 2);
		grid.Children.Add(_contents); grid.Children.Add(splitter); grid.Children.Add(_viewer); Content = grid;
		Opened += (_, _) => _contents.Focus();
	}
	private static string Resource(string key) => Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is string text ? text : key;
	private static string Slug(string heading) => string.Concat(heading.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-')).Replace(' ', '-');
	private void BuildDocument(StackPanel document)
	{
		using Stream stream = typeof(ManualWindow).Assembly.GetManifestResourceStream("Documents/MANUAL.md") ?? throw new InvalidOperationException("The manual resource is missing.");
		using var reader = new StreamReader(stream);
		var rows = new List<TreeViewItem>(); var parents = new Stack<(int Level, TreeViewItem Item)>();
		string? line; bool comment = false;
		while ((line = reader.ReadLine()) != null)
		{
			string text = line.Trim();
			if (comment) { if (text.Contains("-->", StringComparison.Ordinal)) comment = false; continue; }
			if (text.StartsWith("<!--", StringComparison.Ordinal)) { comment = !text.Contains("-->", StringComparison.Ordinal); continue; }
			if (text.Length == 0) { document.Children.Add(new Border { Height = 4 }); continue; }
			int level = text.TakeWhile(c => c == '#').Count();
			if (level > 0 && level < text.Length && text[level] == ' ')
			{
				string title = text[(level + 1)..];
				var heading = new SelectableTextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = level switch { 1 => 24, 2 => 20, _ => 16 }, Margin = new Thickness(0, 14, 0, 4), TextWrapping = TextWrapping.Wrap };
				document.Children.Add(heading); _headings.TryAdd(Slug(title), heading);
				if (level > 1)
				{
					var item = new TreeViewItem { Header = title, Tag = heading, IsExpanded = true };
					while (parents.Count > 0 && parents.Peek().Level >= level) parents.Pop();
					if (parents.Count == 0) rows.Add(item); else parents.Peek().Item.Items.Add(item);
					parents.Push((level, item));
				}
				continue;
			}
			if (text.StartsWith("![", StringComparison.Ordinal) && text.Contains("](Assets/", StringComparison.Ordinal))
			{
				int start = text.IndexOf("](Assets/", StringComparison.Ordinal) + 9, end = text.IndexOf(')', start);
				if (end > start) document.Children.Add(new ManualFigure(text[start..end]));
				continue;
			}
			var paragraph = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
			AppendInline(paragraph, text);
			document.Children.Add(paragraph);
		}
		_contents.ItemsSource = rows;
		if (rows.Count > 0) _contents.SelectedItem = rows[0];
	}
	private void AppendInline(SelectableTextBlock paragraph, string text)
	{
		int previous = 0;
		foreach (Match match in InlinePattern.Matches(text))
		{
			if (match.Index > previous) paragraph.Inlines!.Add(new Run(text[previous..match.Index]));
			string token = match.Value;
			if (token.StartsWith("**", StringComparison.Ordinal)) paragraph.Inlines!.Add(new Run(token[2..^2]) { FontWeight = FontWeight.Bold });
			else if (token.StartsWith('`')) paragraph.Inlines!.Add(new Run(token[1..^1]) { FontFamily = FontFamily.Parse("monospace") });
			else
			{
				int split = token.IndexOf("](", StringComparison.Ordinal);
				string label = token[1..split], target = token[(split + 2)..^1];
				var link = new HyperlinkButton { Content = label, Padding = new Thickness(0), MinHeight = 0, VerticalAlignment = VerticalAlignment.Center };
				if (target.StartsWith('#')) link.Command = new ControlCommand(() => { if (_headings.TryGetValue(target[1..], out Control? heading)) heading.BringIntoView(); }, () => true);
				else if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri)) link.NavigateUri = uri;
				paragraph.Inlines!.Add(new InlineUIContainer { Child = link });
			}
			previous = match.Index + match.Length;
		}
		if (previous < text.Length) paragraph.Inlines!.Add(new Run(text[previous..]));
	}
	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
		base.OnKeyDown(e);
	}
}

/// <summary>A manual SVG figure: its embedded screenshot and numbered badges, rasterised on first display. UI thread only.</summary>
internal sealed class ManualFigure : Control
{
	private readonly XDocument _document;
	private readonly double _width, _height;
	private Bitmap? _bitmap;
	public ManualFigure(string name)
	{
		using Stream stream = typeof(ManualFigure).Assembly.GetManifestResourceStream("ManualFigures/" + name) ?? throw new InvalidOperationException("The manual figure is missing: " + name);
		_document = XDocument.Load(stream);
		XElement root = _document.Root!;
		_width = double.Parse(root.Attribute("width")!.Value, CultureInfo.InvariantCulture);
		_height = double.Parse(root.Attribute("height")!.Value, CultureInfo.InvariantCulture);
		MaxWidth = _width; HorizontalAlignment = HorizontalAlignment.Left;
		AutomationProperties.SetName(this, Path.GetFileNameWithoutExtension(name));
	}
	protected override Size MeasureOverride(Size availableSize)
	{
		double width = double.IsFinite(availableSize.Width) ? Math.Min(_width, availableSize.Width) : _width;
		return new Size(width, width * _height / _width);
	}
	public override void Render(DrawingContext context)
	{
		base.Render(context);
		_bitmap ??= RenderFigure();
		context.DrawImage(_bitmap, new Rect(Bounds.Size));
	}
	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _bitmap?.Dispose(); _bitmap = null; base.OnDetachedFromVisualTree(e); }
	private Bitmap RenderFigure()
	{
		XElement root = _document.Root!;
		XElement image = root.Descendants().First(element => element.Name.LocalName == "image");
		string uri = image.Attributes().First(attribute => attribute.Name.LocalName == "href").Value;
		byte[] encoded = Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);
		using SKBitmap screenshot = SKBitmap.Decode(encoded) ?? throw new InvalidDataException("The manual figure cannot be decoded.");
		float[] viewBox = root.Attribute("viewBox")!.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
		using var surface = new SKBitmap((int)Math.Ceiling(viewBox[2]), (int)Math.Ceiling(viewBox[3]), SKColorType.Bgra8888, SKAlphaType.Premul);
		using var canvas = new SKCanvas(surface);
		canvas.Clear(SKColors.Transparent);
		canvas.Translate(-viewBox[0], -viewBox[1]);
		float imageX = float.Parse(image.Attribute("x")!.Value, CultureInfo.InvariantCulture), imageY = float.Parse(image.Attribute("y")!.Value, CultureInfo.InvariantCulture);
		float imageWidth = float.Parse(image.Attribute("width")!.Value, CultureInfo.InvariantCulture), imageHeight = float.Parse(image.Attribute("height")!.Value, CultureInfo.InvariantCulture);
		canvas.DrawBitmap(screenshot, new SKRect(imageX, imageY, imageX + imageWidth, imageY + imageHeight));
		using var fill = new SKPaint { Color = new SKColor(255, 193, 7), IsAntialias = true };
		using var border = new SKPaint { Color = new SKColor(32, 32, 32), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.25f };
		using var label = new SKPaint { Color = SKColors.Black, IsAntialias = true };
		using SKTypeface face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
		using var font = new SKFont(face, 13.75f);
		foreach (XElement group in root.Descendants().Where(element => element.Name.LocalName == "g" && element.Elements().Any(child => child.Name.LocalName == "circle")))
		{
			XElement circle = group.Elements().First(element => element.Name.LocalName == "circle");
			XElement text = group.Elements().First(element => element.Name.LocalName == "text");
			float x = float.Parse(circle.Attribute("cx")!.Value, CultureInfo.InvariantCulture), y = float.Parse(circle.Attribute("cy")!.Value, CultureInfo.InvariantCulture);
			float radius = float.Parse(circle.Attribute("r")!.Value, CultureInfo.InvariantCulture);
			canvas.DrawCircle(x, y, radius, fill); canvas.DrawCircle(x, y, radius, border);
			canvas.DrawText(text.Value, x - font.MeasureText(text.Value) / 2, y + font.Size * .36f, font, label);
		}
		canvas.Flush();
		using SKImage raster = SKImage.FromBitmap(surface);
		using SKData png = raster.Encode(SKEncodedImageFormat.Png, 100);
		using var bytes = new MemoryStream(png.ToArray());
		return new Bitmap(bytes);
	}
}
