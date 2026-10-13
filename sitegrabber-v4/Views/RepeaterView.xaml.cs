// language: C#, file: Views/RepeaterView.xaml.cs

using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SiteGrabberV3.Views;

public partial class RepeaterView : UserControl
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect      = false,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    }) { Timeout = TimeSpan.FromSeconds(30) };

    public RepeaterView() => InitializeComponent();

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        var url = TxtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        var method  = (CmbMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
        var rawText = TxtRawRequest.Text;

        // Split headers from body on blank line
        var parts   = rawText.Split(["\r\n\r\n", "\n\n"], 2, StringSplitOptions.None);
        var headers = parts.Length > 0 ? parts[0] : "";
        var body    = parts.Length > 1 ? parts[1] : "";

        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        foreach (var line in headers.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf(':');
            if (idx < 0) continue;
            var hName = line[..idx].Trim();
            var hVal  = line[(idx + 1)..].Trim();
            try { req.Headers.TryAddWithoutValidation(hName, hVal); } catch { }
        }

        if (!string.IsNullOrEmpty(body) && method != "GET" && method != "HEAD")
            req.Content = new StringContent(body, Encoding.UTF8);

        LblStatus.Text = "…";
        LblTime.Text   = "…";
        LblSize.Text   = "…";
        TxtResponse.Text = "";

        var sw = Stopwatch.StartNew();
        try
        {
            using var res = await _http.SendAsync(req);
            sw.Stop();
            var resBody = await res.Content.ReadAsStringAsync();

            LblStatus.Text = $"{(int)res.StatusCode} {res.ReasonPhrase}";
            LblTime.Text   = $"{sw.ElapsedMilliseconds} ms";
            LblSize.Text   = $"{resBody.Length / 1024.0:F1} KB";

            var sb = new StringBuilder();
            foreach (var h in res.Headers)
                sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
            foreach (var h in res.Content.Headers)
                sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
            sb.AppendLine();
            sb.Append(resBody.Length > 50_000 ? resBody[..50_000] + "\r\n[truncated]" : resBody);
            TxtResponse.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            sw.Stop();
            TxtResponse.Text = $"[ERROR] {ex.Message}";
            LblStatus.Text   = "Error";
            LblTime.Text     = $"{sw.ElapsedMilliseconds} ms";
        }
    }
}
