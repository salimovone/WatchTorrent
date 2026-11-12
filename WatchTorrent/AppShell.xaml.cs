using WatchTorrent.Views;

namespace WatchTorrent
{
    using Services;
    public partial class AppShell : Shell
    {
        internal Services.VoiceCall Call { get; } = new();
        internal async Task OpenInvitationAsync(Stream stream)
        {
            if (CurrentPage is not VoiceCallPage) await GoToAsync("//connection/call");
            await Call.ImportAsync(stream);
        }

        internal void ShowConnection(bool connected)
        {
            VoiceTab.Title = connected ? "Ulangan" : "Ulanish";
            ConnectionTab.Title = VoiceTab.Title;
        }

        private readonly DownloadService downloads;
        private Page VisiblePage => Navigation.ModalStack.LastOrDefault() ?? CurrentPage ?? this;
        private Guid dialogId;
        private MovieChoicePage? dialog;
        private WatchSession? session;
        private MovieInvite? outgoing;

        private async Task<string> ChooseAsync(MovieInvite movie, bool local, bool waiting, string? file)
        {
            dialogId = movie.Session;
            var page = dialog = new MovieChoicePage(movie, local, waiting, file);
            try
            {
                await Navigation.PushModalAsync(page);
                if (waiting) Call.Invite(movie);
                return await page.Result.Task.WaitAsync(TimeSpan.FromMinutes(2), Call.Token);
            }
            finally
            {
                if (Navigation.ModalStack.LastOrDefault() == page) await Navigation.PopModalAsync();
                dialog = null;
                dialogId = Guid.Empty;
            }
        }

        private void OnControl(WatchMessage m)
        {
            if (dialogId == m.Session && m.Action is "accepted" or "download" or "decline" or "cancel")
                dialog?.Result.TrySetResult(m.Action);
            if (m.Action == "download" && outgoing?.Session == m.Session && dialogId == m.Session)
            {
                if (!downloads.PartnerDownloads.Any(p => p.Id == m.Session))
                    downloads.PartnerDownloads.Add(new(m.Session, outgoing.Title));
            }
            if (downloads.PartnerDownloads.FirstOrDefault(p => p.Id == m.Session) is { } report)
            {
                if (m.Action == "progress") report.Percent = (int)Math.Clamp(m.Position, 0, 100);
                if (m.Action == "removed") downloads.PartnerDownloads.Remove(report);
            }
        }

        internal async Task<WatchSession?> InviteMovieAsync(string magnet, string title, Models.TorrentPageData? page)
        {
            if (dialog != null || outgoing != null || session is { Closed: false }) return null;
            var movie = MovieInvite.Create(magnet, title);
            outgoing = movie;
            try
            {
                movie = await downloads.DescribeMovieAsync(movie, page);
                if (dialog != null || !Call.Connected) return null;
                var answer = await ChooseAsync(movie, false, true, null);
                if (answer == "accepted" && Call.Connected) return session = new(Call, movie.Session, true);
                if (answer == "download")
                {
                    await EnsureMovieDownloadAsync(movie);
                    await VisiblePage.DisplayAlertAsync("Sherigingiz yuklab olishni tanladi", $"{title}\nYuklanish foizini Yuklamalar bo‘limida ko‘rishingiz mumkin.", "OK");
                }
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { await VisiblePage.DisplayAlertAsync("Film taklifi", "Javob kutilgan vaqt tugadi.", "OK"); }
            catch (Exception ex) { await VisiblePage.DisplayAlertAsync("Film taklifi", ex.Message, "OK"); }
            finally { outgoing = null; if (session?.Id != movie.Session) Call.Send(new(movie.Session, "cancel")); }
            return null;
        }

        private async void OnMovieReceived(MovieInvite movie, CancellationToken token)
        {
            if (dialog != null || session is { Closed: false } || movie.Session == Guid.Empty)
            {
                Call.Send(new(movie.Session, "decline"));
                return;
            }
            try
            {
                await downloads.InitializeAsync();
                token.ThrowIfCancellationRequested();
                var item = downloads.FindByMagnet(movie.Magnet);
                var file = movie.SelectFile(item);
                bool local = file != null && file.BitField.AllTrue && File.Exists(file.FullPath);
                var choice = await ChooseAsync(movie, local, false, file == null ? null : Path.GetFileName(file.Path));
                token.ThrowIfCancellationRequested();
                if (choice == "download")
                {
                    var download = await EnsureMovieDownloadAsync(movie);
                    Call.Send(new(movie.Session, "download"));
                    await ReportProgressAsync(movie.Session, download, token);
                    return;
                }
                if (choice != "watch") { Call.Send(new(movie.Session, "decline")); return; }
                if (CurrentPage is PlayerPage player) await player.LeaveAsync();
                var shared = session = new(Call, movie.Session, false);
                Call.Send(new(movie.Session, "accepted"));
                var parameters = new ShellNavigationQueryParameters { ["Title"] = movie.Title, ["FileIndex"] = movie.FileIndex, ["Session"] = shared };
                if (local) parameters["InfoHash"] = item!.InfoHash; else parameters["Magnet"] = movie.Magnet;
                await GoToAsync(nameof(PlayerPage), parameters);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (session?.Id == movie.Session) session.Close();
                Call.Send(new(movie.Session, "decline"));
                await VisiblePage.DisplayAlertAsync("Film taklifi", ex.Message, "OK");
            }
        }

        private async Task<DownloadItem> EnsureMovieDownloadAsync(MovieInvite movie)
        {
            var existing = downloads.FindByMagnet(movie.Magnet);
            var localFile = movie.SelectFile(existing);
            if (existing != null && localFile != null && localFile.BitField.AllTrue && File.Exists(localFile.FullPath))
                return existing;

            var download = await downloads.AddAsync(movie.Magnet, movie.Title);
            var selected = movie.SelectFile(download);
            if (selected?.Priority == MonoTorrent.Priority.DoNotDownload)
            {
                await download.Manager.SetFilePriorityAsync(selected, MonoTorrent.Priority.Normal);
                await downloads.SaveAsync();
            }
            return download;
        }

        private async Task ReportProgressAsync(Guid id, DownloadItem item, CancellationToken token)
        {
            int previous = -1;
            do
            {
                await Task.Delay(1000, token);
                if (!downloads.Items.Contains(item)) { Call.Send(new(id, "removed")); return; }
                int percent = item.IsComplete ? 100 : (int)Math.Clamp(item.Progress * 100, 0, 99);
                if (previous != percent) Call.Send(new(id, "progress", percent));
                previous = percent;
                if (percent == 100) return;
            } while (true);
        }

        public AppShell(DownloadService downloads)
        {
            this.downloads = downloads;
            InitializeComponent();
            Call.MovieReceived += OnMovieReceived;
            Call.ControlReceived += OnControl;
            Call.Changed += () =>
            {
                if (Call.Active) return;
                downloads.PartnerDownloads.Clear();
            };
            ((VoiceCallPage)ConnectionTab.Content).Bind(Call);
            Call.Changed += () => ShowConnection(Call.Connected);

            Routing.RegisterRoute(nameof(TorrentDetailPage), typeof(TorrentDetailPage));
            Routing.RegisterRoute(nameof(DescriptionPage), typeof(DescriptionPage));
            Routing.RegisterRoute(nameof(PlayerPage), typeof(PlayerPage));
            Routing.RegisterRoute(nameof(ImageViewerPage), typeof(ImageViewerPage));
            Routing.RegisterRoute(nameof(DirectConnectionTestPage), typeof(DirectConnectionTestPage));
            Routing.RegisterRoute(nameof(SearchPage), typeof(SearchPage));
        }
    }
}
