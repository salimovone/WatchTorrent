using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using WatchTorrent.Services;
using WatchTorrent.Views;

namespace WatchTorrent
{
    // SupportsPictureInPicture: player kichik oynada (PiP) o'ynay oladi. ConfigurationChanges'dagi ScreenSize/ScreenLayout
    // tufayli kichik oynaga o'tishda activity qayta yaratilmaydi.
    [IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataMimeType = VoiceCallPage.InviteMime, DataScheme = "content")]
    // Download managers often use numeric content URIs and a generic MIME type.
    [IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataMimeType = "application/octet-stream", DataScheme = "content")]
    [IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataMimeType = "*/*", DataScheme = "content", DataHost = "*", DataPathPattern = @".*\.wtcall")]
    [IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataScheme = "content", DataHost = "*", DataPathPattern = @".*\.wtcall")]
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask, SupportsPictureInPicture = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private Intent? invitation;
        private const int PickFolderRequest = 7620, StorageAccessRequest = 7621;
        private TaskCompletionSource<Intent?>? folderResult;

        private async Task<Intent?> StartFolderActivity(Intent intent, int request)
        {
            var result = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
            folderResult = result;
            try
            {
                StartActivityForResult(intent, request);
                return await result.Task;
            }
            finally { folderResult = null; }
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode is PickFolderRequest or StorageAccessRequest)
                folderResult?.TrySetResult(resultCode == Result.Ok ? data : null);
        }

        internal async Task<string?> ChooseMovieFolderAsync()
        {
            var picker = new Intent(Intent.ActionOpenDocumentTree);
            var movies = Android.Provider.DocumentsContract.BuildDocumentUri("com.android.externalstorage.documents", "primary:Movies");
            picker.PutExtra(Android.Provider.DocumentsContract.ExtraInitialUri, movies);
            var result = await StartFolderActivity(picker, PickFolderRequest);
            if (result?.Data is not { } tree) return null;
            var documentId = Android.Provider.DocumentsContract.GetTreeDocumentId(tree);
            if (tree.Authority == "com.android.providers.downloads.documents")
            {
                if (documentId == "downloads") return Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
                if (documentId?.StartsWith("raw:/", StringComparison.Ordinal) == true) return documentId[4..];
            }
            if (tree.Authority != "com.android.externalstorage.documents") throw new NotSupportedException();
            var id = documentId?.Split(':', 2);
            if (id?.Length != 2) throw new NotSupportedException();
            var root = id[0].Equals("primary", StringComparison.OrdinalIgnoreCase)
                ? Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
                : (GetExternalFilesDirs(null) ?? []).Select(f => f?.AbsolutePath?.Split("/Android/", 2)[0])
                    .FirstOrDefault(p => string.Equals(Path.GetFileName(p), id[0], StringComparison.OrdinalIgnoreCase));
            if (root == null) throw new NotSupportedException();
            return Path.Combine(root, id[1]);
        }

        internal async Task<bool> RequestFolderAccessAsync()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(30))
                return await Permissions.RequestAsync<Permissions.StorageWrite>() == PermissionStatus.Granted;
            var intent = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
                Android.Net.Uri.Parse("package:" + PackageName));
            await StartFolderActivity(intent, StorageAccessRequest);
            return Android.OS.Environment.IsExternalStorageManager;
        }

        private void ReadInvitation(Intent? intent)
        {
            if (intent?.Action == Intent.ActionView
                && intent.Data is { Scheme: "content" } uri && uri.Authority != null
                && PackageManager?.ResolveContentProvider(uri.Authority, 0)?.ApplicationInfo?.Uid != Android.OS.Process.MyUid())
                invitation = intent;
        }

        private bool HasInviteName(Android.Net.Uri uri)
        {
            if (uri.LastPathSegment?.EndsWith(".wtcall", StringComparison.OrdinalIgnoreCase) == true) return true;
            using var cursor = ContentResolver?.Query(uri, ["_display_name"], null, null, null);
            var index = cursor?.GetColumnIndex("_display_name") ?? -1;
            return index >= 0 && cursor!.MoveToFirst()
                && cursor.GetString(index)?.EndsWith(".wtcall", StringComparison.OrdinalIgnoreCase) == true;
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            ReadInvitation(intent);
        }

        protected override void OnPostResume()
        {
            base.OnPostResume();
            if (invitation is not { Data: { } uri } request || Shell.Current is not { } shell) return;
            invitation = null;
            if (shell.IsLoaded) OpenFile();
            else shell.Loaded += WhenLoaded;

            void WhenLoaded(object? sender, EventArgs args)
            {
                shell.Loaded -= WhenLoaded;
                OpenFile();
            }

            async void OpenFile()
            {
                try
                {
                    if (request.Type != VoiceCallPage.InviteMime && !HasInviteName(uri))
                    {
                        await shell.DisplayAlertAsync("Ovozli aloqa", "WatchTorrent taklif yoki javob uchun .wtcall faylini ochadi.", "Yopish");
                        return;
                    }
                    using var stream = ContentResolver?.OpenInputStream(uri) ?? throw new IOException();
                    await ((AppShell)shell).OpenInvitationAsync(stream);
                }
                catch (Exception)
                {
                    await shell.DisplayAlertAsync("Ovozli aloqa", "Faylni ochib bo‘lmadi. Ovozli aloqa sahifasidan faylni tanlang.", "Yopish");
                }
            }
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            if (savedInstanceState == null) ReadInvitation(Intent);
            if (Window is not { } window) return;
            var background = Android.Graphics.Color.Rgb(17, 18, 19);
            window.SetStatusBarColor(background);
            window.SetNavigationBarColor(background);
            window.DecorView.SystemUiVisibility &= ~(Android.Views.StatusBarVisibility)(8192 | 16);
        }

        public override void OnConfigurationChanged(Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            PlayerWindow.RefreshAppLayout();
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);

            // Player ochiq bo'lsa, dialog yopilgach qaytib chiqqan tizim panellari yana yashiriladi.
            PlayerWindow.OnWindowFocusChanged(hasFocus);
        }

        protected override void OnUserLeaveHint()
        {
            base.OnUserLeaveHint();

            // Film o'ynab turganda "Home" bosildi — kichik oynaga o'tiladi.
            PictureInPicture.OnUserLeaveHint();
        }

        [SupportedOSPlatform("android26.0")]
        public override void OnPictureInPictureModeChanged(bool isInPictureInPictureMode, Configuration? newConfig)
        {
            base.OnPictureInPictureModeChanged(isInPictureInPictureMode, newConfig);

            PictureInPicture.OnModeChanged(isInPictureInPictureMode);
        }
    }
}
