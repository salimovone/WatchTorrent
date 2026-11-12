namespace WatchTorrent.Views;

public partial class MovieChoicePage : ContentPage
{
    internal TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal MovieChoicePage(Services.MovieInvite movie, bool local, bool waiting, string? file)
    {
        InitializeComponent();
        MovieTitle.Text = movie.Title;
        var info = movie.Details;
        Show(Tags, info?.Tags);
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(info?.Size) && info.Size != "—") facts.Add($"Torrent hajmi: {info.Size}");
        if (info?.FileCount > 0) facts.Add($"{info.FileCount} ta fayl");
        Show(SizeAndFiles, string.Join(" · ", facts));
        Show(Peers, info?.Seeds is int seeds && info.Leeches is int leeches
            ? $"{(info.CatalogCounts ? "Katalogda" : "Yuboruvchida ulangan")} · {seeds} seed · {leeches} leech" : null);
        var video = info?.SelectedVideo ?? file;
        Show(SelectedFile, video != null && (info?.FileCount > 1 || video != movie.Title)
            ? string.Join(" · ", new[] { video, info?.VideoSize }.Where(s => !string.IsNullOrEmpty(s))) : null);
        _ = LoadPosterAsync(movie);
        Details.Text = waiting ? "Sherigingizning javobi kutilmoqda…" :
            local ? "Film bu telefonga yuklangan." : "Birga ko‘rish yoki keyinroq yuklab olishni tanlang.";
        Waiting.IsRunning = Waiting.IsVisible = waiting;
        Watch.IsVisible = !waiting;
        Watch.Text = local ? "Ko‘rish" : "Stream qilib ko‘rish";
        Download.IsVisible = !waiting && !local;
        Cancel.Text = waiting || local ? "Bekor qilish" : "Rad etish";
    }
    private static void Show(Label label, string? text)
    {
        label.Text = text;
        label.IsVisible = !string.IsNullOrWhiteSpace(text);
    }
    private async Task LoadPosterAsync(Services.MovieInvite movie)
    {
        var url = movie.Details?.Poster;
        if (string.IsNullOrEmpty(url) && IPlatformApplication.Current?.Services.GetService<Services.PosterService>() is { } posters)
            url = (await posters.FindAsync(movie.Title))?.Sized(300);
        if (!Result.Task.IsCompleted && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            Poster.Source = ImageSource.FromUri(uri);
            Poster.IsVisible = true;
        }
    }
    private void ChooseWatch(object? sender, EventArgs args) => Result.TrySetResult("watch");
    private void ChooseDownload(object? sender, EventArgs args) => Result.TrySetResult("download");
    private void ChooseCancel(object? sender, EventArgs args) => Result.TrySetResult("decline");
    protected override bool OnBackButtonPressed() { Result.TrySetResult("decline"); return true; }
}
