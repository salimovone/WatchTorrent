namespace WatchTorrent.Views;

public sealed class DirectConnectionTestPage : ContentPage
{
    private readonly WebView browser = new();

    public DirectConnectionTestPage()
    {
        Title = "Direct ulanish testi";
        Content = browser;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        browser.Source = "direct-connection.html";
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        // Leaving this route unloads the document and invokes its pagehide cleanup.
        browser.Source = new HtmlWebViewSource { Html = "<html></html>" };
        base.OnNavigatedFrom(args);
    }
}
