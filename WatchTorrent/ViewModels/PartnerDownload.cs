namespace WatchTorrent.ViewModels;

public sealed class PartnerDownload(Guid id, string title) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Title { get; } = title;
    private int percent;
    public int Percent
    {
        get => percent;
        internal set { if (SetProperty(ref percent, value)) OnPropertyChanged(nameof(Fraction)); }
    }
    public double Fraction => Percent / 100d;
}
