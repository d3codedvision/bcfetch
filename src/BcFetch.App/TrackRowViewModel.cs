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

    public event PropertyChangedEventHandler PropertyChanged;

    public string Name => _file.Name;
    public double Maximum { get; }
    public double Value { get => _value; private set => Set(ref _value, value); }
    public bool Indeterminate { get => _indeterminate; private set => Set(ref _indeterminate, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public IBrush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    /// <param name="token">Looks up a theme brush by token name.</param>
    /// <param name="force">Recompute text and colour even if the state did not change (language or theme switch).</param>
    public void Update(Func<string, IBrush> token, bool force = false)
    {
        var state = _file.State;
        var received = _file.BytesReceived;
        var sized = _file.Size > 0;

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

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
