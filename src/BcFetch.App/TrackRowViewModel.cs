using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using BcFetch.Bandcamp.Download;

namespace BcFetch.App;

/// <summary>
/// What one row of the track list shows. The list is virtualized, so only the rows on screen exist as controls;
/// every row here is cheap, and a change is raised only when a value really changed.
/// </summary>
internal sealed class TrackRowViewModel : INotifyPropertyChanged
{
    private readonly TrackFile _file;
    private readonly List<TrackRowViewModel> _children; // set on an album header row only
    private readonly string _headerText;
    private string _detail;
    private TrackFileState? _shownState;
    private int _shownPercent = -1;
    private double _value;
    private bool _indeterminate;
    private string _status = "";
    private IBrush _statusBrush;

    public TrackRowViewModel(TrackFile file)
    {
        _file = file;
        Maximum = Math.Max(file.Size, 1);
    }

    /// <summary>An album header row; its status counts the settled tracks of <paramref name="children"/>.</summary>
    public TrackRowViewModel(string album, List<TrackRowViewModel> children)
    {
        _headerText = album;
        _children = children;
        Maximum = 1;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public bool IsHeader => _children != null;
    public string Display => IsHeader ? _headerText : _file.Title ?? _file.Name;
    public string Album => IsHeader ? _headerText : _file.Album ?? "";
    public string Folder => _file?.Folder;
    public string Detail { get => _detail; private set { if (Set(ref _detail, value)) OnChanged(nameof(HasDetail)); } }
    public Avalonia.Thickness RowMargin => IsHeader ? new(0, 10, 0, 3) : new(12, 3, 0, 3);
    public Avalonia.Media.FontWeight DisplayWeight => IsHeader ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
    public bool HasDetail => !string.IsNullOrEmpty(_detail);

    public string Name => IsHeader ? _headerText : _file.Name;
    public bool Settled => !IsHeader && _file.State is TrackFileState.Done or TrackFileState.Skipped or TrackFileState.Failed;
    public bool Failed => !IsHeader && _file.State == TrackFileState.Failed;
    public TrackFileState State => _file?.State ?? TrackFileState.Waiting;
    public double Maximum { get; }
    public double Value { get => _value; private set => Set(ref _value, value); }
    public bool Indeterminate { get => _indeterminate; private set => Set(ref _indeterminate, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public IBrush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    /// <param name="token">Looks up a theme brush by token name.</param>
    /// <param name="force">Recompute text and colour even if the state did not change (language or theme switch).</param>
    public void Update(Func<string, IBrush> token, bool force = false)
    {
        if (IsHeader)
        {
            var settled = _children.Count(c => c.Settled);
            var text = $"{settled}/{_children.Count}";
            if (force || text != Status)
            {
                Status = text;
                StatusBrush = token(_children.Any(c => c.Failed) ? "BcDanger" : settled == _children.Count ? "BcSuccess" : "BcMuted");
            }

            return;
        }

        var state = _file.State;
        var received = _file.BytesReceived;
        var sized = _file.Size > 0;

        Detail = state == TrackFileState.Failed ? _file.Error : null;
        Indeterminate = state == TrackFileState.Downloading && !sized;
        Value = state switch
        {
            TrackFileState.Done or TrackFileState.Skipped => Maximum,
            TrackFileState.Downloading => received,
            _ => 0,
        };

        var percent = state == TrackFileState.Downloading && sized ? (int)Math.Min(100, received * 100 / _file.Size) : -1;
        if (!force && state == _shownState && percent == _shownPercent)
        {
            return;
        }

        _shownState = state;
        _shownPercent = percent;
        Status = state switch
        {
            TrackFileState.Waiting => Tr.Get("trackWaiting"),
            TrackFileState.Downloading => sized ? $"{percent}%" : "…",
            TrackFileState.Done => Tr.Get("trackDone"),
            TrackFileState.Skipped => Tr.Get("trackSkipped"),
            _ => Tr.Get("trackFailed"),
        };
        StatusBrush = token(state switch
        {
            TrackFileState.Failed => "BcDanger",
            TrackFileState.Done => "BcSuccess",
            _ => "BcMuted",
        });
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
