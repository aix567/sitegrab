// language: C#, file: Converters/ValueConverters.cs
// IValueConverter implementations used in XAML bindings

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SiteGrabberV3.Models;

namespace SiteGrabberV3.Converters;

// ── Severity → background brush ───────────────────────────────────────────────
public class SeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Severity sev)
        {
            return sev switch
            {
                Severity.Critical => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                Severity.High     => new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)),
                Severity.Medium   => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
                Severity.Low      => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                _                 => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            };
        }
        return Brushes.Gray;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

// ── HTTP Method → background brush ────────────────────────────────────────────
public class MethodToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value as string)?.ToUpperInvariant() switch
        {
            "GET"    => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            "POST"   => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            "PUT"    => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
            "DELETE" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            "PATCH"  => new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7)),
            _        => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

// ── Subdomain status → background brush ───────────────────────────────────────
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "Active"    => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            "Suspended" => new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08)),
            "Error"     => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            _           => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

// ── HTTP Status code → brush (2xx green, 3xx blue, 4xx/5xx red) ───────────────
public class HttpStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int code)
        {
            if (code >= 200 && code < 300) return new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            if (code >= 300 && code < 400) return new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            if (code >= 400 && code < 500) return new SolidColorBrush(Color.FromRgb(0xEA, 0xB3, 0x08));
            if (code >= 500)               return new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        }
        return Brushes.Gray;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
