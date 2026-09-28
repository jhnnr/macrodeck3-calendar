using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeckCalendar;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var plugin = MacroDeckPlugin.CreatePlugin(args)
            .UseMacroDeckLogging()
            .RegisterIntegration<CalendarIntegration>()
            .Build();

        await plugin.RunAsync();
    }
}

public sealed class CalendarIntegration : IPluginIntegration, IWidgetTypeProvider, IUiProvider
{
    private readonly UiState<DateTime> _monthState = new(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
    private readonly UiState<DateTime> _selectedDate = new(DateTime.Today);

    public string ProviderName => "Calendar";

    public IReadOnlyList<IActionDefinition> Actions { get; }

    public CalendarIntegration()
    {
        Actions =
        [
            new CalendarAction("next-month", "Next Month", "Navigate to next month", () =>
            {
                _monthState.Value = _monthState.Peek().AddMonths(1);
            }),
            new CalendarAction("prev-month", "Previous Month", "Navigate to previous month", () =>
            {
                _monthState.Value = _monthState.Peek().AddMonths(-1);
            }),
            new CalendarAction("today", "Current Month", "Navigate to current month and select today", () =>
            {
                _monthState.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                _selectedDate.Value = DateTime.Today;
            })
        ];
    }

    public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

    public Task ShutdownAsync() => Task.CompletedTask;

    // --- IWidgetTypeProvider Implementation ---
    public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
    {
        await context.RegisterWidgetTypeAsync(
            new WidgetTypeDescriptor(
                Id: "calendar",
                Name: "Calendar",
                Description: "Modern interactive monthly calendar widget with day selection, month navigation, and today highlighting.",
                DefaultData: "{}",
                DataSchema: null,
                HasConfiguration: false),
            cancellationToken);
    }

    public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() =>
    [
        new WidgetTypeDescriptor(
            Id: "calendar",
            Name: "Calendar",
            Description: "Modern interactive monthly calendar widget with day selection, month navigation, and today highlighting.",
            DefaultData: "{}",
            DataSchema: null,
            HasConfiguration: false)
    ];

    // --- IUiProvider Implementation ---
    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
    [
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared }
    ];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        if (request.Surface.Kind != UiSurfaceKinds.Widget && request.Surface.Kind != UiSurfaceKinds.Preview)
        {
            return Task.FromResult<IUiSession?>(null);
        }

        var root = BuildCalendarWidget();
        var view = new UiView(request.Surface, root);
        return Task.FromResult<IUiSession?>(new ViewSession(view));
    }

    // --- Modern Declarative UI Widget Tree ---
    private UiElement BuildCalendarWidget()
    {
        var rootChildren = new List<UiElement>();

        // 1. Header Navigation Bar: [ ‹ ]  [ September 2026 ]  [ › ]
        rootChildren.Add(new UiStack
        {
            Key = "header-bar",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Center,
            Justify = UiComponentJustify.SpaceBetween,
            MainSize = 0.11,
            Gap = 0.01,
            Children =
            [
                new UiButton
                {
                    Key = "btn-prev",
                    MainSize = 0.12,
                    Background = "#151d30",
                    Justify = UiComponentJustify.Center,
                    Align = UiComponentAlignments.Center,
                    Events = [UiEventHandler.On(UiComponentEvents.Press, () => _monthState.Value = _monthState.Peek().AddMonths(-1))],
                    Children =
                    [
                        new UiTextRun
                        {
                            Key = "txt-prev",
                            Text = "‹",
                            Size = 0.07,
                            Weight = UiComponentTextWeights.Bold,
                            Color = "#94a3b8",
                            Align = UiComponentAlignments.Center
                        }
                    ]
                },
                new UiButton
                {
                    Key = "btn-title",
                    Fill = true,
                    Background = "#151d30",
                    Direction = UiComponentDirections.Horizontal,
                    Justify = UiComponentJustify.Center,
                    Align = UiComponentAlignments.Center,
                    Events = [UiEventHandler.On(UiComponentEvents.Press, () =>
                    {
                        _monthState.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                        _selectedDate.Value = DateTime.Today;
                    })],
                    Children =
                    [
                        new UiTextRun
                        {
                            Key = "txt-title",
                            Text = UiText.From(() => FormatMonthYear(_monthState.Value)),
                            Size = 0.052,
                            Weight = UiComponentTextWeights.Bold,
                            Color = "#f8fafc",
                            Align = UiComponentAlignments.Center
                        }
                    ]
                },
                new UiButton
                {
                    Key = "btn-next",
                    MainSize = 0.12,
                    Background = "#151d30",
                    Justify = UiComponentJustify.Center,
                    Align = UiComponentAlignments.Center,
                    Events = [UiEventHandler.On(UiComponentEvents.Press, () => _monthState.Value = _monthState.Peek().AddMonths(1))],
                    Children =
                    [
                        new UiTextRun
                        {
                            Key = "txt-next",
                            Text = "›",
                            Size = 0.07,
                            Weight = UiComponentTextWeights.Bold,
                            Color = "#94a3b8",
                            Align = UiComponentAlignments.Center
                        }
                    ]
                }
            ]
        });

        // 2. Weekday Header Row (SUN - SAT with weekend accents)
        string[] weekDays = ["S", "M", "T", "W", "T", "F", "S"];
        var dayHeaderChildren = new List<UiElement>();
        for (int d = 0; d < 7; d++)
        {
            bool isWeekend = d is 0 or 6;
            dayHeaderChildren.Add(new UiTextRun
            {
                Key = $"wd-{d}",
                Text = weekDays[d],
                Size = 0.04,
                Weight = UiComponentTextWeights.SemiBold,
                Color = isWeekend ? "#fb7185" : "#64748b",
                Align = UiComponentAlignments.Center
            });
        }

        rootChildren.Add(new UiGrid
        {
            Key = "weekday-header",
            Columns = 7,
            Rows = 1,
            MainSize = 0.045,
            Gap = 0.006,
            Children = dayHeaderChildren
        });

        // 3. Interactive 42-Cell Days Grid (7 columns x 6 rows)
        var gridCells = new List<UiElement>();
        for (int i = 0; i < 42; i++)
        {
            int cellIndex = i;
            gridCells.Add(new UiButton
            {
                Key = $"btn-day-{cellIndex}",
                Padding = 0.005,
                Background = UiValue.From(() => GetCellBackground(cellIndex, _monthState.Value, _selectedDate.Value)),
                BorderStyle = UiValue.Optional(() => GetCellBorderStyle(cellIndex, _monthState.Value, _selectedDate.Value) is { } s ? s : UiValue.None<string>()),
                BorderColor = UiValue.Optional(() => GetCellBorderColor(cellIndex, _monthState.Value, _selectedDate.Value) is { } c ? c : UiValue.None<string>()),
                Events =
                [
                    UiEventHandler.On(UiComponentEvents.Press, () =>
                    {
                        var info = GetCellInfo(cellIndex, _monthState.Peek(), _selectedDate.Peek());
                        _selectedDate.Value = info.Date;
                        if (!info.InMonth)
                        {
                            _monthState.Value = new DateTime(info.Date.Year, info.Date.Month, 1);
                        }
                    })
                ],
                Children =
                [
                    new UiTextRun
                    {
                        Key = $"txt-day-{cellIndex}",
                        Text = UiText.From(() => GetCellDay(cellIndex, _monthState.Value)),
                        Size = 0.046,
                        Weight = UiValue.From(() => GetCellTextWeight(cellIndex, _monthState.Value, _selectedDate.Value)),
                        Color = UiValue.From(() => GetCellTextColor(cellIndex, _monthState.Value, _selectedDate.Value)),
                        Align = UiComponentAlignments.Center
                    }
                ]
            });
        }

        rootChildren.Add(new UiGrid
        {
            Key = "days-grid",
            Columns = 7,
            Rows = 6,
            Fill = true,
            Gap = 0.007,
            Children = gridCells
        });

        // 4. Footer Information & Quick-Jump Bar
        rootChildren.Add(new UiStack
        {
            Key = "footer-bar",
            Direction = UiComponentDirections.Horizontal,
            Align = UiComponentAlignments.Center,
            Justify = UiComponentJustify.SpaceBetween,
            MainSize = 0.065,
            Padding = 0.005,
            Children =
            [
                new UiTextRun
                {
                    Key = "selected-date-label",
                    Text = UiText.From(() => _selectedDate.Value.ToString("ddd, MMM d, yyyy")),
                    Size = 0.038,
                    Weight = UiComponentTextWeights.Medium,
                    Color = "#94a3b8"
                },
                new UiButton
                {
                    Key = "btn-footer-today",
                    Padding = 0.006,
                    Background = UiValue.From(() => IsViewingCurrentMonth(_monthState.Value) ? "#151d30" : "#2563eb"),
                    Events = [UiEventHandler.On(UiComponentEvents.Press, () =>
                    {
                        _monthState.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                        _selectedDate.Value = DateTime.Today;
                    })],
                    Children =
                    [
                        new UiTextRun
                        {
                            Key = "btn-footer-today-text",
                            Text = "TODAY",
                            Size = 0.034,
                            Weight = UiComponentTextWeights.Bold,
                            Color = "#f8fafc",
                            Align = UiComponentAlignments.Center
                        }
                    ]
                }
            ]
        });

        return new UiStack
        {
            Key = "calendar-root",
            Direction = UiComponentDirections.Vertical,
            Background = "#0b0f19",
            Gap = 0.012,
            Padding = 0.02,
            Children = rootChildren
        };
    }

    // --- Date Calculation & Aesthetic Styling Helpers ---
    private static (DateTime Date, bool InMonth, bool IsToday, bool IsSelected, bool IsWeekend) GetCellInfo(
        int cellIndex, DateTime currentMonth, DateTime selectedDate)
    {
        int offset = (int)currentMonth.DayOfWeek; // Sunday = 0
        DateTime startDate = currentMonth.AddDays(-offset);
        DateTime cellDate = startDate.AddDays(cellIndex);
        bool inMonth = cellDate.Month == currentMonth.Month && cellDate.Year == currentMonth.Year;
        bool isToday = cellDate.Date == DateTime.Today;
        bool isSelected = cellDate.Date == selectedDate.Date;
        bool isWeekend = cellDate.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday;
        return (cellDate, inMonth, isToday, isSelected, isWeekend);
    }

    private static string GetCellDay(int cellIndex, DateTime currentMonth)
    {
        int offset = (int)currentMonth.DayOfWeek;
        return currentMonth.AddDays(-offset + cellIndex).Day.ToString();
    }

    private static string GetCellBackground(int cellIndex, DateTime currentMonth, DateTime selectedDate)
    {
        var info = GetCellInfo(cellIndex, currentMonth, selectedDate);
        if (info.IsToday) return "#2563eb"; // Vibrant primary blue
        if (info.IsSelected) return "#1e293b"; // Sleek selected slate
        return info.InMonth ? "#141b2d" : "#0d1322"; // Active in-month vs out-of-month recessed tile
    }

    private static string? GetCellBorderColor(int cellIndex, DateTime currentMonth, DateTime selectedDate)
    {
        var info = GetCellInfo(cellIndex, currentMonth, selectedDate);
        if (info.IsToday) return "#93c5fd"; // Sky blue ring on today
        if (info.IsSelected) return "#38bdf8"; // Cyan ring on selected
        return null;
    }

    private static string? GetCellBorderStyle(int cellIndex, DateTime currentMonth, DateTime selectedDate)
    {
        var info = GetCellInfo(cellIndex, currentMonth, selectedDate);
        if (info.IsToday || info.IsSelected) return UiComponentBorderStyles.Static;
        return null;
    }

    private static string GetCellTextColor(int cellIndex, DateTime currentMonth, DateTime selectedDate)
    {
        var info = GetCellInfo(cellIndex, currentMonth, selectedDate);
        if (info.IsToday) return "#ffffff";
        if (info.IsSelected) return "#38bdf8";
        if (!info.InMonth) return "#475569";
        return info.IsWeekend ? "#fda4af" : "#f1f5f9"; // Soft rose on weekends, crisp white-slate on weekdays
    }

    private static string GetCellTextWeight(int cellIndex, DateTime currentMonth, DateTime selectedDate)
    {
        var info = GetCellInfo(cellIndex, currentMonth, selectedDate);
        if (info.IsToday || info.IsSelected) return UiComponentTextWeights.Bold;
        return info.InMonth ? UiComponentTextWeights.Medium : UiComponentTextWeights.Regular;
    }

    private static bool IsViewingCurrentMonth(DateTime currentMonth)
        => currentMonth.Month == DateTime.Today.Month && currentMonth.Year == DateTime.Today.Year;

    private static string FormatMonthYear(DateTime date)
    {
        string month = date.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture);
        if (month.Length > 0)
        {
            month = char.ToUpperInvariant(month[0]) + month[1..].ToLowerInvariant();
        }
        return $"{month} {date.Year}";
    }
}

public sealed class CalendarAction(string id, string name, string description, Action execute) : IActionDefinition
{
    public string Id => id;
    public MacroDeck.Localization.LocalizedText Name => name;
    public MacroDeck.Localization.LocalizedText Description => description;
    public IReadOnlyList<ActionParameter> Parameters => [];
    public IActionExecutor CreateExecutor() => new Executor(execute);

    private sealed class Executor(Action execute) : IActionExecutor
    {
        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            execute();
            return ActionResult.SucceededTask;
        }
    }
}

public sealed class ViewSession : IUiSession
{
    private readonly UiView _view;

    public ViewSession(UiView view)
    {
        _view = view;
        _view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _view.HandlerFaulted += (_, fault)
            => Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
    }

    public event EventHandler? Changed;

    public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

    public UiTree BuildTree() => _view.Tree;

    public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

    public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

    public ValueTask DisposeAsync()
    {
        _view.Dispose();
        return ValueTask.CompletedTask;
    }
}
