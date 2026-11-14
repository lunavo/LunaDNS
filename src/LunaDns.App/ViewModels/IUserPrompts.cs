namespace LunaDns.App.ViewModels;

/// <summary>Hộp thoại cho VM, để VM không tham chiếu Window.</summary>
internal interface IUserPrompts
{
    Task<bool> ConfirmAsync(string title, string message);

    Task InformAsync(string title, string message);
}
