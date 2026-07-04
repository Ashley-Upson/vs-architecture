using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using StandardIo.ArchitectureDiagram.Core.Renderers;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Vsix;

[ComVisible(true)]
[Guid("6f14da51-b4dc-4cf4-8cb6-ce571489f70c")]
public sealed class DiagramOptionsPage : DialogPage
{
    private DiagramOptionsControl? _control;
    private DiagramSettings _settings = DiagramSettings.CreateDefault();

    public DiagramOptionsPage()
    {
        DiagnosticLog.Write("DiagramOptionsPage constructed.");
    }

    protected override IWin32Window Window
    {
        get
        {
            DiagnosticLog.Write("DiagramOptionsPage.Window requested.");

            try
            {
                DiagnosticLog.Write(_control is null
                    ? "Creating DiagramOptionsControl."
                    : "Reusing existing DiagramOptionsControl.");

                _control ??= new DiagramOptionsControl();
                _control.LoadSettings(_settings);
                DiagnosticLog.Write("DiagramOptionsPage.Window returning DiagramOptionsControl.");
                return _control;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("DiagramOptionsPage.Window failed.", ex);

                var errorControl = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    Text = ex + Environment.NewLine + Environment.NewLine + "Diagnostic log: " + DiagnosticLog.FilePath
                };

                return errorControl;
            }
        }
    }

    public override void LoadSettingsFromStorage()
    {
        DiagnosticLog.Write("DiagramOptionsPage.LoadSettingsFromStorage started.");

        try
        {
            _settings = SettingsStore.Load();
            _control?.LoadSettings(_settings);
            DiagnosticLog.Write("DiagramOptionsPage.LoadSettingsFromStorage completed.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("DiagramOptionsPage.LoadSettingsFromStorage failed; using defaults.", ex);
            _settings = DiagramSettings.CreateDefault();
        }
    }

    public override void SaveSettingsToStorage()
    {
        DiagnosticLog.Write("DiagramOptionsPage.SaveSettingsToStorage started.");

        try
        {
            if (_control is not null)
            {
                _settings = _control.ToSettings();
            }

            SettingsStore.Save(_settings);
            DiagnosticLog.Write("DiagramOptionsPage.SaveSettingsToStorage completed.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("DiagramOptionsPage.SaveSettingsToStorage failed.", ex);
            // Avoid Visual Studio replacing the whole page with "An error occurred".
        }
    }
}

internal sealed class DiagramOptionsControl : UserControl
{
    private readonly TextBox _backgroundColor = TextBox();
    private readonly ComboBox _outputRenderer = RendererBox();
    private readonly TextBox _defaultFontColor = TextBox();
    private readonly NumericUpDown _nodeWidth = NumberBox(40, 800);
    private readonly NumericUpDown _nodeHeight = NumberBox(30, 400);
    private readonly NumericUpDown _horizontalSpacing = NumberBox(0, 600);
    private readonly NumericUpDown _verticalSpacing = NumberBox(0, 600);
    private readonly NumericUpDown _containerPadding = NumberBox(0, 300);
    private readonly CheckBox _showProjectContainers = new() { Text = "Show project containers", AutoSize = true };
    private readonly TextBox _connectorColor = TextBox();
    private readonly NumericUpDown _connectorWidth = NumberBox(1, 20);
    private readonly CheckBox _connectorRounded = new() { Text = "Rounded connectors", AutoSize = true };
    private readonly TextBox _excludedNamespaces = MultilineTextBox();
    private readonly TextBox _excludedNames = MultilineTextBox();
    private readonly DataGridView _styleRules = StyleGrid(includeMatcher: true);
    private readonly DataGridView _overrides = StyleGrid(includeMatcher: false);
    private readonly StyleEditor _projectStyle = new("Project Container Style");
    private readonly StyleEditor _externalStyle = new("External Dependency Style");

    public DiagramOptionsControl()
    {
        DiagnosticLog.Write("DiagramOptionsControl construction started.");

        Dock = DockStyle.Fill;
        Padding = new Padding(10);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateGeneralTab());
        tabs.TabPages.Add(CreateRulesTab());
        tabs.TabPages.Add(CreateSpecialStylesTab());
        Controls.Add(tabs);

        LoadSettings(DiagramSettings.CreateDefault());
        DiagnosticLog.Write("DiagramOptionsControl construction completed.");
    }

    public void LoadSettings(DiagramSettings settings)
    {
        DiagnosticLog.Write("DiagramOptionsControl.LoadSettings started.");
        settings = Normalize(settings);

        SetRenderer(settings.OutputRenderer);
        _backgroundColor.Text = settings.Canvas.BackgroundColor;
        _defaultFontColor.Text = settings.Canvas.DefaultFontColor;
        _nodeWidth.Value = Clamp(settings.Layout.NodeWidth, _nodeWidth);
        _nodeHeight.Value = Clamp(settings.Layout.NodeHeight, _nodeHeight);
        _horizontalSpacing.Value = Clamp(settings.Layout.HorizontalSpacing, _horizontalSpacing);
        _verticalSpacing.Value = Clamp(settings.Layout.VerticalSpacing, _verticalSpacing);
        _containerPadding.Value = Clamp(settings.Layout.ContainerPadding, _containerPadding);
        _showProjectContainers.Checked = settings.ShowProjectContainers;
        _connectorColor.Text = settings.Connector.StrokeColor;
        _connectorWidth.Value = Clamp(settings.Connector.StrokeWidth, _connectorWidth);
        _connectorRounded.Checked = settings.Connector.Rounded;
        _excludedNamespaces.Text = string.Join(Environment.NewLine, settings.ExcludedNamespaces);
        _excludedNames.Text = string.Join(Environment.NewLine, settings.ExcludedNames);
        LoadRules(settings.StyleRules);
        LoadOverrides(settings.Overrides);
        _projectStyle.LoadStyle(settings.ProjectContainerStyle);
        _externalStyle.LoadStyle(settings.ExternalDependencyStyle);
        DiagnosticLog.Write("DiagramOptionsControl.LoadSettings completed.");
    }

    public DiagramSettings ToSettings()
    {
        DiagnosticLog.Write("DiagramOptionsControl.ToSettings started.");

        return new DiagramSettings
        {
            Canvas = new CanvasSettings
            {
                BackgroundColor = _backgroundColor.Text.Trim(),
                DefaultFontColor = _defaultFontColor.Text.Trim()
            },
            OutputRenderer = Convert.ToString(_outputRenderer.SelectedItem)?.Trim() ?? DiagramRendererIds.Drawio,
            Layout = new StandardIo.ArchitectureDiagram.Core.Settings.LayoutSettings
            {
                NodeWidth = (int)_nodeWidth.Value,
                NodeHeight = (int)_nodeHeight.Value,
                HorizontalSpacing = (int)_horizontalSpacing.Value,
                VerticalSpacing = (int)_verticalSpacing.Value,
                ContainerPadding = (int)_containerPadding.Value
            },
            Connector = new ConnectorStyle
            {
                StrokeColor = _connectorColor.Text.Trim(),
                StrokeWidth = (int)_connectorWidth.Value,
                Rounded = _connectorRounded.Checked
            },
            ExcludedNamespaces = Lines(_excludedNamespaces),
            ExcludedNames = Lines(_excludedNames),
            StyleRules = ReadRules(),
            Overrides = ReadOverrides(),
            ShowProjectContainers = _showProjectContainers.Checked,
            ProjectContainerStyle = _projectStyle.ToStyle(),
            ExternalDependencyStyle = _externalStyle.ToStyle()
        };
    }

    private static DiagramSettings Normalize(DiagramSettings? settings)
    {
        settings ??= DiagramSettings.CreateDefault();
        settings.Canvas ??= new CanvasSettings();
        settings.Layout ??= new StandardIo.ArchitectureDiagram.Core.Settings.LayoutSettings();
        settings.Connector ??= new ConnectorStyle();
        settings.OutputRenderer = string.IsNullOrWhiteSpace(settings.OutputRenderer)
            ? DiagramRendererIds.Drawio
            : settings.OutputRenderer.Trim();
        settings.ExcludedNamespaces ??= new();
        settings.ExcludedNames ??= new();
        settings.StyleRules ??= new();
        settings.Overrides ??= new();
        settings.ProjectContainerStyle ??= NodeStyle.ProjectContainer();
        settings.ExternalDependencyStyle ??= NodeStyle.External();
        return settings;
    }

    private static decimal Clamp(int value, NumericUpDown control) =>
        Math.Min(control.Maximum, Math.Max(control.Minimum, value));

    private TabPage CreateGeneralTab()
    {
        var page = new TabPage("General");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(panel, "Output renderer", _outputRenderer);
        AddRow(panel, "Background color", _backgroundColor);
        AddRow(panel, "Default font color", _defaultFontColor);
        AddRow(panel, "Node width", _nodeWidth);
        AddRow(panel, "Node height", _nodeHeight);
        AddRow(panel, "Horizontal spacing", _horizontalSpacing);
        AddRow(panel, "Vertical spacing", _verticalSpacing);
        AddRow(panel, "Container padding", _containerPadding);
        AddRow(panel, string.Empty, _showProjectContainers);
        AddRow(panel, "Connector color", _connectorColor);
        AddRow(panel, "Connector width", _connectorWidth);
        AddRow(panel, string.Empty, _connectorRounded);
        AddRow(panel, "Excluded namespaces", _excludedNamespaces, 90);
        AddRow(panel, "Excluded names", _excludedNames, 90);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage CreateRulesTab()
    {
        var page = new TabPage("Rules");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 260 };
        split.Panel1.Controls.Add(Section("Ordered Style Rules", _styleRules));
        split.Panel2.Controls.Add(Section("Exact Full Name Overrides", _overrides));
        page.Controls.Add(split);
        return page;
    }

    private TabPage CreateSpecialStylesTab()
    {
        var page = new TabPage("Special Styles");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 230 };
        split.Panel1.Controls.Add(_projectStyle);
        split.Panel2.Controls.Add(_externalStyle);
        page.Controls.Add(split);
        return page;
    }

    private void LoadRules(IEnumerable<StyleRule> rules)
    {
        _styleRules.Rows.Clear();
        foreach (var rule in rules)
        {
            AddStyleRow(_styleRules, rule.Match, rule.Style);
        }
    }

    private void LoadOverrides(IEnumerable<StyleOverride> overrides)
    {
        _overrides.Rows.Clear();
        foreach (var item in overrides)
        {
            AddStyleRow(_overrides, item.FullName, item.Style);
        }
    }

    private List<StyleRule> ReadRules() =>
        ReadStyleRows(_styleRules).Select(x => new StyleRule { Match = x.Match, Style = x.Style }).ToList();

    private List<StyleOverride> ReadOverrides() =>
        ReadStyleRows(_overrides).Select(x => new StyleOverride { FullName = x.Match, Style = x.Style }).ToList();

    private static IEnumerable<(string Match, NodeStyle Style)> ReadStyleRows(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var match = Cell(row, 0);
            if (string.IsNullOrWhiteSpace(match))
            {
                continue;
            }

            yield return (match, new NodeStyle
            {
                FillColor = Cell(row, 1),
                StrokeColor = Cell(row, 2),
                FontColor = Cell(row, 3),
                Shape = Cell(row, 4),
                Shadow = row.Cells[5].Value is bool value && value,
                ExtraStyle = EmptyToNull(Cell(row, 6))
            });
        }
    }

    private static void AddStyleRow(DataGridView grid, string match, NodeStyle style)
    {
        style ??= new NodeStyle();
        grid.Rows.Add(match, style.FillColor, style.StrokeColor, style.FontColor, style.Shape, style.Shadow, style.ExtraStyle);
    }

    private static string Cell(DataGridViewRow row, int index) => Convert.ToString(row.Cells[index].Value)?.Trim() ?? string.Empty;

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<string> Lines(TextBox box) =>
        box.Lines.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

    private static GroupBox Section(string title, Control child)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8) };
        child.Dock = DockStyle.Fill;
        box.Controls.Add(child);
        return box;
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control, int height = 32)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
    }

    private static TextBox TextBox() => new() { BorderStyle = BorderStyle.FixedSingle };

    private static ComboBox RendererBox()
    {
        var box = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        box.Items.Add(DiagramRendererIds.Drawio);
        box.Items.Add(DiagramRendererIds.Json);
        return box;
    }

    private void SetRenderer(string rendererId)
    {
        if (!_outputRenderer.Items.Contains(rendererId))
        {
            _outputRenderer.Items.Add(rendererId);
        }

        _outputRenderer.SelectedItem = rendererId;
    }

    private static TextBox MultilineTextBox() => new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical
    };

    private static NumericUpDown NumberBox(int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        DecimalPlaces = 0
    };

    private static DataGridView StyleGrid(bool includeMatcher)
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            RowHeadersWidth = 24
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = includeMatcher ? "Match" : "Full Name" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fill" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Stroke" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Font" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Shape" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Shadow" });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Extra Style" });
        return grid;
    }
}

internal sealed class StyleEditor : GroupBox
{
    private readonly TextBox _fillColor = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _strokeColor = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _fontColor = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _shape = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox _shadow = new() { Text = "Shadow", AutoSize = true };
    private readonly TextBox _extraStyle = new() { BorderStyle = BorderStyle.FixedSingle, Multiline = true, ScrollBars = ScrollBars.Vertical };

    public StyleEditor(string title)
    {
        Text = title;
        Dock = DockStyle.Fill;
        Padding = new Padding(8);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(panel, "Fill color", _fillColor);
        AddRow(panel, "Stroke color", _strokeColor);
        AddRow(panel, "Font color", _fontColor);
        AddRow(panel, "Shape", _shape);
        AddRow(panel, string.Empty, _shadow);
        AddRow(panel, "Extra style", _extraStyle, 76);
        Controls.Add(panel);
    }

    public void LoadStyle(NodeStyle style)
    {
        _fillColor.Text = style.FillColor;
        _strokeColor.Text = style.StrokeColor;
        _fontColor.Text = style.FontColor;
        _shape.Text = style.Shape;
        _shadow.Checked = style.Shadow;
        _extraStyle.Text = style.ExtraStyle ?? string.Empty;
    }

    public NodeStyle ToStyle() => new()
    {
        FillColor = _fillColor.Text.Trim(),
        StrokeColor = _strokeColor.Text.Trim(),
        FontColor = _fontColor.Text.Trim(),
        Shape = _shape.Text.Trim(),
        Shadow = _shadow.Checked,
        ExtraStyle = string.IsNullOrWhiteSpace(_extraStyle.Text) ? null : _extraStyle.Text.Trim()
    };

    private static void AddRow(TableLayoutPanel panel, string label, Control control, int height = 32)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
    }
}
