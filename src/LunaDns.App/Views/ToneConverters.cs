using Avalonia.Data.Converters;
using LunaDns.App.ViewModels;

namespace LunaDns.App.Views;

/// <summary>Bật class style theo Tone, ví dụ Classes.good="{Binding Tone, Converter={x:Static v:ToneConverters.IsGood}}". Neutral là style gốc, không cần class.</summary>
internal static class ToneConverters
{
    public static readonly IValueConverter IsInfo = Is(Tone.Info);
    public static readonly IValueConverter IsGood = Is(Tone.Good);
    public static readonly IValueConverter IsWarning = Is(Tone.Warning);
    public static readonly IValueConverter IsError = Is(Tone.Error);
    public static readonly IValueConverter IsBusy = Is(Tone.Busy);

    /// <summary>Ẩn icon khi mức là Neutral (dòng chữ thường, không kèm icon).</summary>
    public static readonly IValueConverter IsNotNeutral = new FuncValueConverter<Tone, bool>(value => value != Tone.Neutral);

    private static FuncValueConverter<Tone, bool> Is(Tone tone) => new(value => value == tone);
}
