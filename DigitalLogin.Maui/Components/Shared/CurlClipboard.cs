using Microsoft.JSInterop;
using Radzen;

namespace DigitalLogin.Maui.Components.Shared;

/// <summary>Copies a curl command to the clipboard and tells the user the result.</summary>
public static class CurlClipboard
{
    public static async Task CopyAsync(IJSRuntime js, NotificationService notifications, string curl)
    {
        try
        {
            await js.InvokeVoidAsync("digitalLoginStore.copy", curl);
            notifications.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Copied",
                Detail = "cURL copied to the clipboard.",
                Duration = 3000,
            });
        }
        catch (Microsoft.JSInterop.JSException)
        {
            notifications.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = "Copy failed",
                Detail = "The clipboard is not available.",
                Duration = 4000,
            });
        }
    }
}
